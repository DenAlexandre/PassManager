using Microsoft.EntityFrameworkCore;
using PassManager.Application.Common;
using PassManager.Domain.Abstractions;
using PassManager.Domain.Entities;

namespace PassManager.Application.Sync;

public class SyncService(IPassManagerDbContext db, IClock clock)
{
    private const int PageSize = 500;

    public async Task<PullResult> PullAsync(Guid ownerId, DateTimeOffset? since, CancellationToken ct = default)
    {
        var requestStart = clock.UtcNow;
        var sinceValue = since ?? DateTimeOffset.MinValue;

        var folders = await db.Folders
            .Where(f => f.OwnerId == ownerId && f.UpdatedAt > sinceValue)
            .OrderBy(f => f.UpdatedAt)
            .Take(PageSize)
            .ToListAsync(ct);

        var entries = await db.Entries
            .Where(e => e.OwnerId == ownerId && e.UpdatedAt > sinceValue)
            .OrderBy(e => e.UpdatedAt)
            .Take(PageSize)
            .ToListAsync(ct);

        var cappedTimestamps = new List<DateTimeOffset>();
        if (folders.Count == PageSize)
        {
            cappedTimestamps.Add(folders.Max(f => f.UpdatedAt));
        }
        if (entries.Count == PageSize)
        {
            cappedTimestamps.Add(entries.Max(e => e.UpdatedAt));
        }

        var hasMore = cappedTimestamps.Count > 0;
        var nextCursor = hasMore ? cappedTimestamps.Min() : requestStart;

        return new PullResult(
            requestStart,
            folders.Select(ToFolderDto).ToList(),
            entries.Select(ToEntryDto).ToList(),
            nextCursor,
            hasMore);
    }

    public async Task<PushResult> PushAsync(
        Guid ownerId, List<FolderPushItem> folderItems, List<EntryPushItem> entryItems, CancellationToken ct = default)
    {
        var accepted = new List<Guid>();
        var conflicts = new List<PushConflict>();
        var rejected = new List<PushRejected>();
        var now = clock.UtcNow;

        var ownerFolders = await db.Folders.Where(f => f.OwnerId == ownerId).ToDictionaryAsync(f => f.Id, ct);

        foreach (var item in folderItems)
        {
            if (ownerFolders.TryGetValue(item.Id, out var existing))
            {
                if (existing.IsRoot)
                {
                    rejected.Add(new PushRejected(item.Id, "folder", "Le dossier Racine ne peut pas être modifié par synchronisation."));
                    continue;
                }

                if (item.ClientVersion != existing.Version && item.UpdatedAt <= existing.UpdatedAt)
                {
                    conflicts.Add(new PushConflict(item.Id, "folder", ToFolderDto(existing), null));
                    continue;
                }

                if (item.Deleted)
                {
                    existing.DeletedAt = now;
                }
                else
                {
                    if (item.ParentId is null || !IsValidParent(ownerFolders, item.Id, item.ParentId.Value))
                    {
                        rejected.Add(new PushRejected(item.Id, "folder", "Dossier parent invalide ou cycle détecté."));
                        continue;
                    }

                    existing.Name = item.Name;
                    existing.ParentId = item.ParentId;
                    existing.DeletedAt = null;
                }

                existing.Version += 1;
                existing.UpdatedAt = now;
                accepted.Add(item.Id);
            }
            else
            {
                if (item.Deleted)
                {
                    // Server never saw this folder — nothing to tombstone, but acknowledge so the client can drop it.
                    accepted.Add(item.Id);
                    continue;
                }

                if (item.ParentId is null || !IsValidParent(ownerFolders, item.Id, item.ParentId.Value))
                {
                    rejected.Add(new PushRejected(item.Id, "folder", "Dossier parent invalide ou cycle détecté."));
                    continue;
                }

                var newFolder = new Folder
                {
                    Id = item.Id,
                    OwnerId = ownerId,
                    ParentId = item.ParentId,
                    Name = item.Name,
                    IsRoot = false,
                    Version = 1,
                    DeletedAt = item.Deleted ? now : null,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Folders.Add(newFolder);
                ownerFolders[newFolder.Id] = newFolder;
                accepted.Add(item.Id);
            }
        }

        var ownerEntries = await db.Entries.Where(e => e.OwnerId == ownerId).ToDictionaryAsync(e => e.Id, ct);

        foreach (var item in entryItems)
        {
            if (ownerEntries.TryGetValue(item.Id, out var existingEntry))
            {
                if (item.ClientVersion != existingEntry.Version && item.UpdatedAt <= existingEntry.UpdatedAt)
                {
                    conflicts.Add(new PushConflict(item.Id, "entry", null, ToEntryDto(existingEntry)));
                    continue;
                }

                if (item.Deleted)
                {
                    existingEntry.DeletedAt = now;
                }
                else
                {
                    if (!ownerFolders.ContainsKey(item.FolderId))
                    {
                        rejected.Add(new PushRejected(item.Id, "entry", "Dossier cible introuvable."));
                        continue;
                    }

                    existingEntry.FolderId = item.FolderId;
                    existingEntry.Title = item.Title;
                    existingEntry.Url = item.Url;
                    existingEntry.Login = item.Login;
                    existingEntry.Password = item.Password;
                    existingEntry.Memo = item.Memo;
                    existingEntry.DeletedAt = null;
                }

                existingEntry.Version += 1;
                existingEntry.UpdatedAt = now;
                accepted.Add(item.Id);
            }
            else
            {
                if (item.Deleted)
                {
                    // Server never saw this entry — nothing to tombstone, but acknowledge so the client can drop it.
                    accepted.Add(item.Id);
                    continue;
                }

                if (!ownerFolders.ContainsKey(item.FolderId))
                {
                    rejected.Add(new PushRejected(item.Id, "entry", "Dossier cible introuvable."));
                    continue;
                }

                var newEntry = new Entry
                {
                    Id = item.Id,
                    FolderId = item.FolderId,
                    OwnerId = ownerId,
                    Title = item.Title,
                    Url = item.Url,
                    Login = item.Login,
                    Password = item.Password,
                    Memo = item.Memo,
                    Version = 1,
                    DeletedAt = null,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Entries.Add(newEntry);
                ownerEntries[newEntry.Id] = newEntry;
                accepted.Add(item.Id);
            }
        }

        await db.SaveChangesAsync(ct);

        return new PushResult(accepted, conflicts, rejected);
    }

    /// <summary>Ensures parentId exists, belongs to the owner, and is not a descendant of folderId (no cycles).</summary>
    private static bool IsValidParent(Dictionary<Guid, Folder> ownerFolders, Guid folderId, Guid parentId)
    {
        if (!ownerFolders.TryGetValue(parentId, out var current))
        {
            return false;
        }

        var guard = 0;
        while (true)
        {
            if (current.Id == folderId)
            {
                return false;
            }

            if (current.ParentId is null || !ownerFolders.TryGetValue(current.ParentId.Value, out current))
            {
                return true;
            }

            if (++guard > 10_000)
            {
                return false;
            }
        }
    }

    private static FolderSyncDto ToFolderDto(Folder f) =>
        new(f.Id, f.ParentId, f.Name, f.IsRoot, f.Version, f.UpdatedAt, f.DeletedAt.HasValue);

    private static EntrySyncDto ToEntryDto(Entry e) =>
        new(e.Id, e.FolderId, e.Title, e.Url, e.Login, e.Password, e.Memo, e.Version, e.UpdatedAt, e.DeletedAt.HasValue);
}
