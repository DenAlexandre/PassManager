using Microsoft.EntityFrameworkCore;
using PassManager.Application.Sync;
using PassManager.Application.Tests.TestDoubles;
using PassManager.Domain.Entities;
using Xunit;

namespace PassManager.Application.Tests.Sync;

public class SyncServiceTests
{
    private static async Task<(SyncService Service, TestDbContext Db, FakeClock Clock, Guid OwnerId, Guid RootId)> CreateSutWithRootAsync()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var ownerId = Guid.NewGuid();
        var root = new Folder
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = Folder.RootName,
            IsRoot = true,
            Version = 1,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };
        db.Folders.Add(root);
        await db.SaveChangesAsync();

        var service = new SyncService(db, clock);
        return (service, db, clock, ownerId, root.Id);
    }

    [Fact]
    public async Task PullAsync_WithoutCursor_ReturnsAllOwnerData()
    {
        var (service, _, _, ownerId, rootId) = await CreateSutWithRootAsync();

        var result = await service.PullAsync(ownerId, since: null);

        var folder = Assert.Single(result.Folders);
        Assert.Equal(rootId, folder.Id);
        Assert.False(result.HasMore);
    }

    [Fact]
    public async Task PullAsync_WithCursorAfterLastChange_ReturnsNothingNew()
    {
        var (service, _, clock, ownerId, _) = await CreateSutWithRootAsync();
        var firstPull = await service.PullAsync(ownerId, since: null);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var result = await service.PullAsync(ownerId, since: firstPull.NextCursor);

        Assert.Empty(result.Folders);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task PushAsync_NewFolder_IsAcceptedAndPersisted()
    {
        var (service, db, _, ownerId, rootId) = await CreateSutWithRootAsync();
        var newFolderId = Guid.NewGuid();

        var result = await service.PushAsync(
            ownerId,
            [new FolderPushItem(newFolderId, rootId, "Travail", ClientVersion: 0, UpdatedAt: DateTimeOffset.UtcNow, Deleted: false)],
            []);

        Assert.Contains(newFolderId, result.Accepted);
        Assert.Empty(result.Conflicts);
        Assert.Empty(result.Rejected);
        Assert.Equal(2, await db.Folders.CountAsync());
    }

    [Fact]
    public async Task PushAsync_NewFolderWithUnknownParent_IsRejected()
    {
        var (service, _, _, ownerId, _) = await CreateSutWithRootAsync();

        var result = await service.PushAsync(
            ownerId,
            [new FolderPushItem(Guid.NewGuid(), Guid.NewGuid(), "Orphan", 0, DateTimeOffset.UtcNow, false)],
            []);

        Assert.Empty(result.Accepted);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public async Task PushAsync_UpdateWithStaleVersionButNewerTimestamp_StillWins()
    {
        var (service, db, clock, ownerId, rootId) = await CreateSutWithRootAsync();
        var folderId = Guid.NewGuid();
        await service.PushAsync(ownerId, [new FolderPushItem(folderId, rootId, "V1", 0, clock.UtcNow, false)], []);

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        var laterUpdate = new FolderPushItem(folderId, rootId, "V2-client-newer", ClientVersion: 0, UpdatedAt: clock.UtcNow, Deleted: false);
        var result = await service.PushAsync(ownerId, [laterUpdate], []);

        Assert.Contains(folderId, result.Accepted);
        Assert.Equal("V2-client-newer", (await db.Folders.SingleAsync(f => f.Id == folderId)).Name);
    }

    [Fact]
    public async Task PushAsync_UpdateWithStaleVersionAndOlderTimestamp_ReturnsConflictWithServerState()
    {
        var (service, db, clock, ownerId, rootId) = await CreateSutWithRootAsync();
        var folderId = Guid.NewGuid();
        await service.PushAsync(ownerId, [new FolderPushItem(folderId, rootId, "ServerWins", 0, clock.UtcNow, false)], []);
        var serverUpdatedAt = (await db.Folders.SingleAsync(f => f.Id == folderId)).UpdatedAt;

        var staleUpdate = new FolderPushItem(folderId, rootId, "StaleClientEdit", ClientVersion: 0, UpdatedAt: serverUpdatedAt.AddMinutes(-10), Deleted: false);
        var result = await service.PushAsync(ownerId, [staleUpdate], []);

        Assert.Empty(result.Accepted);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("ServerWins", conflict.ServerFolder!.Name);
    }

    [Fact]
    public async Task PushAsync_RootFolder_IsRejected()
    {
        var (service, _, _, ownerId, rootId) = await CreateSutWithRootAsync();

        var result = await service.PushAsync(
            ownerId, [new FolderPushItem(rootId, null, "Hack", 1, DateTimeOffset.UtcNow, false)], []);

        Assert.Empty(result.Accepted);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public async Task PushAsync_FolderMoveCreatingCycle_IsRejected()
    {
        var (service, _, clock, ownerId, rootId) = await CreateSutWithRootAsync();
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        await service.PushAsync(ownerId, [new FolderPushItem(parentId, rootId, "Parent", 0, clock.UtcNow, false)], []);
        await service.PushAsync(ownerId, [new FolderPushItem(childId, parentId, "Child", 0, clock.UtcNow, false)], []);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var moveParentUnderChild = new FolderPushItem(parentId, childId, "Parent", ClientVersion: 1, UpdatedAt: clock.UtcNow, Deleted: false);
        var result = await service.PushAsync(ownerId, [moveParentUnderChild], []);

        Assert.Empty(result.Accepted);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public async Task PushAsync_NewEntry_IsAccepted()
    {
        var (service, db, clock, ownerId, rootId) = await CreateSutWithRootAsync();
        var entryId = Guid.NewGuid();

        var result = await service.PushAsync(
            ownerId, [],
            [new EntryPushItem(entryId, rootId, "GitHub", "https://github.com", "me", "secret", "memo", 0, clock.UtcNow, false)]);

        Assert.Contains(entryId, result.Accepted);
        Assert.Equal("secret", (await db.Entries.SingleAsync()).Password);
    }

    [Fact]
    public async Task PushAsync_EntryDeleteTombstone_IsAcceptedAndExcludedFromNextPull()
    {
        var (service, _, clock, ownerId, rootId) = await CreateSutWithRootAsync();
        var entryId = Guid.NewGuid();
        await service.PushAsync(
            ownerId, [], [new EntryPushItem(entryId, rootId, "ToDelete", null, null, "pwd", null, 0, clock.UtcNow, false)]);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var deleteItem = new EntryPushItem(entryId, rootId, "ToDelete", null, null, "pwd", null, ClientVersion: 1, UpdatedAt: clock.UtcNow, Deleted: true);
        var result = await service.PushAsync(ownerId, [], [deleteItem]);

        Assert.Contains(entryId, result.Accepted);

        var pullAfterDelete = await service.PullAsync(ownerId, since: null);
        var entryDto = Assert.Single(pullAfterDelete.Entries, e => e.Id == entryId);
        Assert.True(entryDto.Deleted);
    }

    [Fact]
    public async Task PushAsync_DeleteOfNeverSyncedFolder_IsAcceptedAsNoOp()
    {
        var (service, db, clock, ownerId, rootId) = await CreateSutWithRootAsync();
        var neverSyncedId = Guid.NewGuid();

        var result = await service.PushAsync(
            ownerId, [new FolderPushItem(neverSyncedId, null, "", 0, clock.UtcNow, Deleted: true)], []);

        Assert.Contains(neverSyncedId, result.Accepted);
        Assert.Empty(result.Rejected);
        Assert.Equal(1, await db.Folders.CountAsync()); // only the root — nothing was inserted
    }

    [Fact]
    public async Task PushAsync_DeleteOfNeverSyncedEntry_IsAcceptedAsNoOp()
    {
        var (service, db, clock, ownerId, rootId) = await CreateSutWithRootAsync();
        var neverSyncedId = Guid.NewGuid();

        var result = await service.PushAsync(
            ownerId, [], [new EntryPushItem(neverSyncedId, Guid.Empty, "", null, null, "", null, 0, clock.UtcNow, Deleted: true)]);

        Assert.Contains(neverSyncedId, result.Accepted);
        Assert.Empty(result.Rejected);
        Assert.Empty(await db.Entries.ToListAsync());
    }

    [Fact]
    public async Task PushAsync_DeleteOfExistingFolder_PreservesNameAndParent()
    {
        var (service, db, clock, ownerId, rootId) = await CreateSutWithRootAsync();
        var folderId = Guid.NewGuid();
        await service.PushAsync(ownerId, [new FolderPushItem(folderId, rootId, "Important", 0, clock.UtcNow, false)], []);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var deleteItem = new FolderPushItem(folderId, null, "", ClientVersion: 1, UpdatedAt: clock.UtcNow, Deleted: true);
        var result = await service.PushAsync(ownerId, [deleteItem], []);

        Assert.Contains(folderId, result.Accepted);
        var deletedFolder = await db.Folders.SingleAsync(f => f.Id == folderId);
        Assert.Equal("Important", deletedFolder.Name);
        Assert.Equal(rootId, deletedFolder.ParentId);
        Assert.NotNull(deletedFolder.DeletedAt);
    }
}
