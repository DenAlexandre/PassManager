using Microsoft.EntityFrameworkCore;
using PassManager.Application.Common;
using PassManager.Domain.Abstractions;
using PassManager.Domain.Entities;

namespace PassManager.Application.Sharing;

public class EntrySharingService(IPassManagerDbContext db, IClock clock)
{
    public async Task<EntryShareResult> ShareAsync(
        Guid requestingUserId, Guid sourceEntryId, Guid targetUserId, CancellationToken ct = default)
    {
        if (requestingUserId == targetUserId)
        {
            return new EntryShareResult(false, EntryShareError.CannotShareToSelf, null);
        }

        var sourceEntry = await db.Entries
            .FirstOrDefaultAsync(e => e.Id == sourceEntryId && e.OwnerId == requestingUserId && e.DeletedAt == null, ct);
        if (sourceEntry is null)
        {
            return new EntryShareResult(false, EntryShareError.EntryNotFound, null);
        }

        var targetRoot = await db.Folders
            .FirstOrDefaultAsync(f => f.OwnerId == targetUserId && f.IsRoot && f.DeletedAt == null, ct);
        if (targetRoot is null)
        {
            return new EntryShareResult(false, EntryShareError.TargetUserNotFound, null);
        }

        var sourceOwnerFolders = await db.Folders
            .Where(f => f.OwnerId == requestingUserId && f.DeletedAt == null)
            .ToListAsync(ct);
        var ancestorChain = FolderTree.CollectAncestorChain(sourceOwnerFolders, sourceEntry.FolderId);

        var requestingUser = await db.Users.FirstAsync(u => u.Id == requestingUserId, ct);
        var now = clock.UtcNow;
        var destinationFolderId = targetRoot.Id;

        if (ancestorChain.Count > 0)
        {
            var existingTopLevelNames = await db.Folders
                .Where(f => f.OwnerId == targetUserId && f.ParentId == targetRoot.Id && f.DeletedAt == null)
                .Select(f => f.Name)
                .ToListAsync(ct);

            var topFolderName = existingTopLevelNames.Contains(ancestorChain[0].Name)
                ? $"{ancestorChain[0].Name} (partagé par {requestingUser.Email})"
                : ancestorChain[0].Name;

            var parentId = targetRoot.Id;
            for (var i = 0; i < ancestorChain.Count; i++)
            {
                var newFolder = new Folder
                {
                    Id = Guid.NewGuid(),
                    OwnerId = targetUserId,
                    ParentId = parentId,
                    Name = i == 0 ? topFolderName : ancestorChain[i].Name,
                    IsRoot = false,
                    Version = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Folders.Add(newFolder);
                parentId = newFolder.Id;
            }

            destinationFolderId = parentId;
        }

        var newEntryId = Guid.NewGuid();
        db.Entries.Add(new Entry
        {
            Id = newEntryId,
            FolderId = destinationFolderId,
            OwnerId = targetUserId,
            Title = sourceEntry.Title,
            Url = sourceEntry.Url,
            Login = sourceEntry.Login,
            Password = sourceEntry.Password,
            Memo = sourceEntry.Memo,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        });

        db.EntryShares.Add(new EntryShare
        {
            Id = Guid.NewGuid(),
            SourceEntryId = sourceEntryId,
            SourceOwnerId = requestingUserId,
            TargetOwnerId = targetUserId,
            SharedAt = now
        });

        await db.SaveChangesAsync(ct);

        return new EntryShareResult(true, EntryShareError.None, newEntryId);
    }
}
