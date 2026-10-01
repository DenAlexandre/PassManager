using PassManager.Core.Abstractions;
using PassManager.Core.Models;
using PassManager.Core.Sync;

namespace PassManager.Core.Vault;

/// <summary>In-memory tree operations over a loaded VaultDocument, with modifiedUtc-based dirty tracking for sync.</summary>
public class VaultRepository(VaultDocument document, IClock clock)
{
    public VaultDocument Document => document;

    public VaultFolder? GetRootFolder() => document.Folders.FirstOrDefault(f => f.IsRoot);

    public IReadOnlyList<VaultFolder> GetFolders() => document.Folders;

    public IReadOnlyList<VaultFolder> GetChildFolders(Guid? parentId) =>
        document.Folders.Where(f => f.ParentId == parentId).ToList();

    public IReadOnlyList<VaultEntry> GetEntries(Guid folderId) =>
        document.Entries.Where(e => e.FolderId == folderId).ToList();

    public VaultFolder CreateFolder(Guid parentId, string name)
    {
        if (document.Folders.All(f => f.Id != parentId))
        {
            throw new InvalidOperationException("Dossier parent introuvable.");
        }

        var folder = new VaultFolder
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Name = name,
            IsRoot = false,
            Version = 0,
            UpdatedAt = clock.UtcNow
        };
        document.Folders.Add(folder);
        return folder;
    }

    public void RenameFolder(Guid folderId, string name)
    {
        var folder = RequireFolder(folderId);
        if (folder.IsRoot)
        {
            throw new InvalidOperationException("Le dossier Racine ne peut pas être renommé.");
        }

        folder.Name = name;
        folder.UpdatedAt = clock.UtcNow;
    }

    public void DeleteFolder(Guid folderId)
    {
        var folder = RequireFolder(folderId);
        if (folder.IsRoot)
        {
            throw new InvalidOperationException("Le dossier Racine ne peut pas être supprimé.");
        }

        var idsToDelete = CollectDescendantIds(folderId);
        var now = clock.UtcNow;

        var foldersToDelete = document.Folders.Where(f => idsToDelete.Contains(f.Id)).ToList();
        document.Folders.RemoveAll(f => idsToDelete.Contains(f.Id));
        foreach (var deletedFolder in foldersToDelete)
        {
            document.Tombstones.Add(new VaultTombstone
            {
                Id = deletedFolder.Id,
                Type = TombstoneType.Folder,
                Version = deletedFolder.Version,
                DeletedAt = now
            });
        }

        var entriesToDelete = document.Entries.Where(e => idsToDelete.Contains(e.FolderId)).ToList();
        document.Entries.RemoveAll(e => idsToDelete.Contains(e.FolderId));
        foreach (var entry in entriesToDelete)
        {
            document.Tombstones.Add(new VaultTombstone
            {
                Id = entry.Id,
                Type = TombstoneType.Entry,
                Version = entry.Version,
                DeletedAt = now
            });
        }
    }

    public VaultEntry CreateEntry(Guid folderId, string title, string? url, string? login, string password, string? memo)
    {
        if (document.Folders.All(f => f.Id != folderId))
        {
            throw new InvalidOperationException("Dossier introuvable.");
        }

        var entry = new VaultEntry
        {
            Id = Guid.NewGuid(),
            FolderId = folderId,
            Title = title,
            Url = url,
            Login = login,
            Password = password,
            Memo = memo,
            Version = 0,
            UpdatedAt = clock.UtcNow
        };
        document.Entries.Add(entry);
        return entry;
    }

    public void UpdateEntry(Guid entryId, string title, string? url, string? login, string password, string? memo)
    {
        var entry = RequireEntry(entryId);
        entry.Title = title;
        entry.Url = url;
        entry.Login = login;
        entry.Password = password;
        entry.Memo = memo;
        entry.UpdatedAt = clock.UtcNow;
    }

    public void DeleteEntry(Guid entryId)
    {
        var entry = RequireEntry(entryId);
        document.Entries.Remove(entry);
        document.Tombstones.Add(new VaultTombstone
        {
            Id = entry.Id,
            Type = TombstoneType.Entry,
            Version = entry.Version,
            DeletedAt = clock.UtcNow
        });
    }

    public IReadOnlyList<VaultFolder> GetFoldersModifiedSince(DateTimeOffset? since) =>
        document.Folders.Where(f => since is null || f.UpdatedAt > since).ToList();

    public IReadOnlyList<VaultEntry> GetEntriesModifiedSince(DateTimeOffset? since) =>
        document.Entries.Where(e => since is null || e.UpdatedAt > since).ToList();

    public IReadOnlyList<VaultTombstone> GetTombstonesSince(DateTimeOffset? since) =>
        document.Tombstones.Where(t => since is null || t.DeletedAt > since).ToList();

    /// <summary>Merges one authoritative folder row from a sync pull into local state (upsert, or remove+tombstone if deleted).</summary>
    public void ApplyPulledFolder(FolderSyncItem item)
    {
        if (item.Deleted)
        {
            document.Folders.RemoveAll(f => f.Id == item.Id);
            if (document.Tombstones.All(t => t.Id != item.Id))
            {
                document.Tombstones.Add(new VaultTombstone { Id = item.Id, Type = TombstoneType.Folder, Version = item.Version, DeletedAt = item.UpdatedAt });
            }
            return;
        }

        document.Tombstones.RemoveAll(t => t.Id == item.Id && t.Type == TombstoneType.Folder);
        var existing = document.Folders.FirstOrDefault(f => f.Id == item.Id);
        if (existing is null)
        {
            document.Folders.Add(new VaultFolder
            {
                Id = item.Id,
                ParentId = item.ParentId,
                Name = item.Name,
                IsRoot = item.IsRoot,
                Version = item.Version,
                UpdatedAt = item.UpdatedAt
            });
        }
        else
        {
            existing.ParentId = item.ParentId;
            existing.Name = item.Name;
            existing.IsRoot = item.IsRoot;
            existing.Version = item.Version;
            existing.UpdatedAt = item.UpdatedAt;
        }
    }

    /// <summary>Merges one authoritative entry row from a sync pull into local state (upsert, or remove+tombstone if deleted).</summary>
    public void ApplyPulledEntry(EntrySyncItem item)
    {
        if (item.Deleted)
        {
            document.Entries.RemoveAll(e => e.Id == item.Id);
            if (document.Tombstones.All(t => t.Id != item.Id))
            {
                document.Tombstones.Add(new VaultTombstone { Id = item.Id, Type = TombstoneType.Entry, Version = item.Version, DeletedAt = item.UpdatedAt });
            }
            return;
        }

        document.Tombstones.RemoveAll(t => t.Id == item.Id && t.Type == TombstoneType.Entry);
        var existing = document.Entries.FirstOrDefault(e => e.Id == item.Id);
        if (existing is null)
        {
            document.Entries.Add(new VaultEntry
            {
                Id = item.Id,
                FolderId = item.FolderId,
                Title = item.Title,
                Url = item.Url,
                Login = item.Login,
                Password = item.Password,
                Memo = item.Memo,
                Version = item.Version,
                UpdatedAt = item.UpdatedAt
            });
        }
        else
        {
            existing.FolderId = item.FolderId;
            existing.Title = item.Title;
            existing.Url = item.Url;
            existing.Login = item.Login;
            existing.Password = item.Password;
            existing.Memo = item.Memo;
            existing.Version = item.Version;
            existing.UpdatedAt = item.UpdatedAt;
        }
    }

    private HashSet<Guid> CollectDescendantIds(Guid rootId)
    {
        var childrenByParent = document.Folders
            .Where(f => f.ParentId.HasValue)
            .GroupBy(f => f.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Id).ToList());

        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (result.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    private VaultFolder RequireFolder(Guid folderId) =>
        document.Folders.FirstOrDefault(f => f.Id == folderId)
            ?? throw new InvalidOperationException("Dossier introuvable.");

    private VaultEntry RequireEntry(Guid entryId) =>
        document.Entries.FirstOrDefault(e => e.Id == entryId)
            ?? throw new InvalidOperationException("Entrée introuvable.");
}
