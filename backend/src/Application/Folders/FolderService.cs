using Microsoft.EntityFrameworkCore;
using PassManager.Application.Common;
using PassManager.Domain.Abstractions;
using PassManager.Domain.Entities;

namespace PassManager.Application.Folders;

public class FolderService(IPassManagerDbContext db, IClock clock)
{
    public async Task<List<Folder>> GetFoldersAsync(Guid ownerId, CancellationToken ct = default) =>
        await db.Folders
            .Where(f => f.OwnerId == ownerId && f.DeletedAt == null)
            .ToListAsync(ct);

    public async Task<FolderResult> CreateFolderAsync(
        Guid ownerId, Guid folderId, Guid parentId, string name, CancellationToken ct = default)
    {
        var alreadyExists = await db.Folders.AnyAsync(f => f.Id == folderId, ct);
        if (alreadyExists)
        {
            return new FolderResult(false, FolderError.DuplicateId, null);
        }

        var parent = await db.Folders
            .FirstOrDefaultAsync(f => f.Id == parentId && f.OwnerId == ownerId && f.DeletedAt == null, ct);
        if (parent is null)
        {
            return new FolderResult(false, FolderError.ParentNotFound, null);
        }

        var now = clock.UtcNow;
        var folder = new Folder
        {
            Id = folderId,
            OwnerId = ownerId,
            ParentId = parentId,
            Name = name,
            IsRoot = false,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync(ct);

        return new FolderResult(true, FolderError.None, folder);
    }

    public async Task<FolderResult> RenameFolderAsync(
        Guid ownerId, Guid folderId, string name, CancellationToken ct = default)
    {
        var folder = await FindOwnedAsync(ownerId, folderId, ct);
        if (folder is null)
        {
            return new FolderResult(false, FolderError.NotFound, null);
        }

        if (folder.IsRoot)
        {
            return new FolderResult(false, FolderError.CannotModifyRoot, null);
        }

        folder.Name = name;
        folder.Version += 1;
        folder.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return new FolderResult(true, FolderError.None, folder);
    }

    public async Task<FolderResult> DeleteFolderAsync(Guid ownerId, Guid folderId, CancellationToken ct = default)
    {
        var folder = await FindOwnedAsync(ownerId, folderId, ct);
        if (folder is null)
        {
            return new FolderResult(false, FolderError.NotFound, null);
        }

        if (folder.IsRoot)
        {
            return new FolderResult(false, FolderError.CannotModifyRoot, null);
        }

        var now = clock.UtcNow;
        var allOwnerFolders = await db.Folders
            .Where(f => f.OwnerId == ownerId && f.DeletedAt == null)
            .ToListAsync(ct);
        var idsToDelete = FolderTree.CollectDescendantIds(allOwnerFolders, folderId);

        foreach (var toDelete in allOwnerFolders.Where(f => idsToDelete.Contains(f.Id)))
        {
            toDelete.DeletedAt = now;
            toDelete.Version += 1;
            toDelete.UpdatedAt = now;
        }

        var entriesToDelete = await db.Entries
            .Where(e => e.OwnerId == ownerId && e.DeletedAt == null && idsToDelete.Contains(e.FolderId))
            .ToListAsync(ct);
        foreach (var entry in entriesToDelete)
        {
            entry.DeletedAt = now;
            entry.Version += 1;
            entry.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return new FolderResult(true, FolderError.None, folder);
    }

    private Task<Folder?> FindOwnedAsync(Guid ownerId, Guid folderId, CancellationToken ct) =>
        db.Folders.FirstOrDefaultAsync(f => f.Id == folderId && f.OwnerId == ownerId && f.DeletedAt == null, ct);
}
