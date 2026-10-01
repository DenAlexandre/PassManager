using Microsoft.EntityFrameworkCore;
using PassManager.Application.Common;
using PassManager.Domain.Abstractions;
using PassManager.Domain.Entities;

namespace PassManager.Application.Entries;

public class EntryService(IPassManagerDbContext db, IClock clock)
{
    public async Task<List<Entry>> GetEntriesAsync(Guid ownerId, Guid folderId, CancellationToken ct = default) =>
        await db.Entries
            .Where(e => e.OwnerId == ownerId && e.FolderId == folderId && e.DeletedAt == null)
            .ToListAsync(ct);

    public async Task<EntryResult> CreateEntryAsync(
        Guid ownerId, Guid entryId, Guid folderId, string title, string? url, string? login, string password, string? memo,
        CancellationToken ct = default)
    {
        var alreadyExists = await db.Entries.AnyAsync(e => e.Id == entryId, ct);
        if (alreadyExists)
        {
            return new EntryResult(false, EntryError.DuplicateId, null);
        }

        var folderExists = await db.Folders
            .AnyAsync(f => f.Id == folderId && f.OwnerId == ownerId && f.DeletedAt == null, ct);
        if (!folderExists)
        {
            return new EntryResult(false, EntryError.FolderNotFound, null);
        }

        var now = clock.UtcNow;
        var entry = new Entry
        {
            Id = entryId,
            FolderId = folderId,
            OwnerId = ownerId,
            Title = title,
            Url = url,
            Login = login,
            Password = password,
            Memo = memo,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Entries.Add(entry);
        await db.SaveChangesAsync(ct);

        return new EntryResult(true, EntryError.None, entry);
    }

    public async Task<EntryResult> UpdateEntryAsync(
        Guid ownerId, Guid entryId, string title, string? url, string? login, string password, string? memo,
        CancellationToken ct = default)
    {
        var entry = await FindOwnedAsync(ownerId, entryId, ct);
        if (entry is null)
        {
            return new EntryResult(false, EntryError.NotFound, null);
        }

        entry.Title = title;
        entry.Url = url;
        entry.Login = login;
        entry.Password = password;
        entry.Memo = memo;
        entry.Version += 1;
        entry.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return new EntryResult(true, EntryError.None, entry);
    }

    public async Task<EntryResult> DeleteEntryAsync(Guid ownerId, Guid entryId, CancellationToken ct = default)
    {
        var entry = await FindOwnedAsync(ownerId, entryId, ct);
        if (entry is null)
        {
            return new EntryResult(false, EntryError.NotFound, null);
        }

        entry.DeletedAt = clock.UtcNow;
        entry.Version += 1;
        entry.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return new EntryResult(true, EntryError.None, entry);
    }

    private Task<Entry?> FindOwnedAsync(Guid ownerId, Guid entryId, CancellationToken ct) =>
        db.Entries.FirstOrDefaultAsync(e => e.Id == entryId && e.OwnerId == ownerId && e.DeletedAt == null, ct);
}
