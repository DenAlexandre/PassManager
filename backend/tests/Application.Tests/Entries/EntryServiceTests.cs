using Microsoft.EntityFrameworkCore;
using PassManager.Application.Entries;
using PassManager.Application.Tests.TestDoubles;
using PassManager.Domain.Entities;
using Xunit;

namespace PassManager.Application.Tests.Entries;

public class EntryServiceTests
{
    private static async Task<(EntryService Service, TestDbContext Db, Guid OwnerId, Guid FolderId)> CreateSutWithFolderAsync()
    {
        var db = TestDbContext.Create();
        var ownerId = Guid.NewGuid();
        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = Folder.RootName,
            IsRoot = true,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();

        var service = new EntryService(db, new FakeClock());
        return (service, db, ownerId, folder.Id);
    }

    [Fact]
    public async Task CreateEntryAsync_InOwnedFolder_Succeeds()
    {
        var (service, db, ownerId, folderId) = await CreateSutWithFolderAsync();

        var result = await service.CreateEntryAsync(
            ownerId, Guid.NewGuid(), folderId, "GitHub", "https://github.com", "me", "secret", "note");

        Assert.True(result.Succeeded);
        var entry = await db.Entries.SingleAsync();
        Assert.Equal("GitHub", entry.Title);
        Assert.Equal("secret", entry.Password);
    }

    [Fact]
    public async Task CreateEntryAsync_WithFolderOwnedByAnotherUser_ReturnsFolderNotFound()
    {
        var (service, _, _, folderId) = await CreateSutWithFolderAsync();
        var attacker = Guid.NewGuid();

        var result = await service.CreateEntryAsync(attacker, Guid.NewGuid(), folderId, "Hack", null, null, "x", null);

        Assert.False(result.Succeeded);
        Assert.Equal(EntryError.FolderNotFound, result.Error);
    }

    [Fact]
    public async Task UpdateEntryAsync_BumpsVersionAndUpdatesFields()
    {
        var (service, _, ownerId, folderId) = await CreateSutWithFolderAsync();
        var created = await service.CreateEntryAsync(ownerId, Guid.NewGuid(), folderId, "Old", null, null, "old-pwd", null);

        var updated = await service.UpdateEntryAsync(
            ownerId, created.Entry!.Id, "New", "https://new.example", "login", "new-pwd", "memo");

        Assert.True(updated.Succeeded);
        Assert.Equal("New", updated.Entry!.Title);
        Assert.Equal("new-pwd", updated.Entry.Password);
        Assert.Equal(2, updated.Entry.Version);
    }

    [Fact]
    public async Task UpdateEntryAsync_OwnedByAnotherUser_ReturnsNotFound()
    {
        var (service, _, ownerId, folderId) = await CreateSutWithFolderAsync();
        var created = await service.CreateEntryAsync(ownerId, Guid.NewGuid(), folderId, "Old", null, null, "old-pwd", null);

        var result = await service.UpdateEntryAsync(Guid.NewGuid(), created.Entry!.Id, "Hack", null, null, "x", null);

        Assert.False(result.Succeeded);
        Assert.Equal(EntryError.NotFound, result.Error);
    }

    [Fact]
    public async Task DeleteEntryAsync_SoftDeletesAndExcludesFromListing()
    {
        var (service, db, ownerId, folderId) = await CreateSutWithFolderAsync();
        var created = await service.CreateEntryAsync(ownerId, Guid.NewGuid(), folderId, "ToDelete", null, null, "pwd", null);

        var deleteResult = await service.DeleteEntryAsync(ownerId, created.Entry!.Id);

        Assert.True(deleteResult.Succeeded);
        Assert.NotNull((await db.Entries.SingleAsync()).DeletedAt);
        Assert.Empty(await service.GetEntriesAsync(ownerId, folderId));
    }
}
