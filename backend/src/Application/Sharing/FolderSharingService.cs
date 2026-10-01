using Microsoft.EntityFrameworkCore;
using PassManager.Application.Common;
using PassManager.Domain.Abstractions;
using PassManager.Domain.Entities;

namespace PassManager.Application.Sharing;

public class FolderSharingService(IPassManagerDbContext db, IClock clock)
{
    public async Task<ShareResult> ShareAsync(
        Guid requestingUserId, Guid sourceFolderId, Guid targetUserId, CancellationToken ct = default)
    {
        if (requestingUserId == targetUserId)
        {
            return new ShareResult(false, ShareError.CannotShareToSelf, null);
        }

        var sourceFolder = await db.Folders
            .FirstOrDefaultAsync(f => f.Id == sourceFolderId && f.OwnerId == requestingUserId && f.DeletedAt == null, ct);
        if (sourceFolder is null)
        {
            return new ShareResult(false, ShareError.FolderNotFound, null);
        }

        if (sourceFolder.IsRoot)
        {
            return new ShareResult(false, ShareError.CannotShareRoot, null);
        }

        var targetRoot = await db.Folders
            .FirstOrDefaultAsync(f => f.OwnerId == targetUserId && f.IsRoot && f.DeletedAt == null, ct);
        if (targetRoot is null)
        {
            return new ShareResult(false, ShareError.TargetUserNotFound, null);
        }

        var sourceOwnerFolders = await db.Folders
            .Where(f => f.OwnerId == requestingUserId && f.DeletedAt == null)
            .ToListAsync(ct);
        var subtreeIds = FolderTree.CollectDescendantIds(sourceOwnerFolders, sourceFolderId);

        var sourceEntries = await db.Entries
            .Where(e => e.OwnerId == requestingUserId && e.DeletedAt == null && subtreeIds.Contains(e.FolderId))
            .ToListAsync(ct);

        var requestingUser = await db.Users.FirstAsync(u => u.Id == requestingUserId, ct);
        var existingTopLevelNames = await db.Folders
            .Where(f => f.OwnerId == targetUserId && f.ParentId == targetRoot.Id && f.DeletedAt == null)
            .Select(f => f.Name)
            .ToListAsync(ct);

        var topLevelName = existingTopLevelNames.Contains(sourceFolder.Name)
            ? $"{sourceFolder.Name} (partagé par {requestingUser.Email})"
            : sourceFolder.Name;

        var now = clock.UtcNow;
        var idMap = subtreeIds.ToDictionary(id => id, _ => Guid.NewGuid());

        var newFolders = sourceOwnerFolders
            .Where(f => subtreeIds.Contains(f.Id))
            .Select(old => new Folder
            {
                Id = idMap[old.Id],
                OwnerId = targetUserId,
                ParentId = old.Id == sourceFolderId ? targetRoot.Id : idMap[old.ParentId!.Value],
                Name = old.Id == sourceFolderId ? topLevelName : old.Name,
                IsRoot = false,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            })
            .ToList();
        db.Folders.AddRange(newFolders);

        var newEntries = sourceEntries
            .Select(old => new Entry
            {
                Id = Guid.NewGuid(),
                FolderId = idMap[old.FolderId],
                OwnerId = targetUserId,
                Title = old.Title,
                Url = old.Url,
                Login = old.Login,
                Password = old.Password,
                Memo = old.Memo,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now
            })
            .ToList();
        db.Entries.AddRange(newEntries);

        db.FolderShares.Add(new FolderShare
        {
            Id = Guid.NewGuid(),
            SourceFolderId = sourceFolderId,
            SourceOwnerId = requestingUserId,
            TargetOwnerId = targetUserId,
            SharedAt = now
        });

        await db.SaveChangesAsync(ct);

        return new ShareResult(true, ShareError.None, idMap[sourceFolderId]);
    }

    public async Task<List<UserSummary>> SearchUsersAsync(Guid requestingUserId, string? query, CancellationToken ct = default)
    {
        const int minQueryLength = 2;
        const int maxResults = 20;

        var trimmed = query?.Trim() ?? "";
        if (trimmed.Length < minQueryLength)
        {
            return [];
        }

        return await db.Users
            .Where(u => u.Id != requestingUserId && u.Email.StartsWith(trimmed))
            .OrderBy(u => u.Email)
            .Take(maxResults)
            .Select(u => new UserSummary(u.Id, u.Email))
            .ToListAsync(ct);
    }
}
