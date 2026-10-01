using Microsoft.EntityFrameworkCore;
using PassManager.Application.Folders;
using PassManager.Application.Tests.TestDoubles;
using PassManager.Domain.Entities;
using Xunit;

namespace PassManager.Application.Tests.Folders;

public class FolderServiceTests
{
    private static async Task<(FolderService Service, TestDbContext Db, Guid OwnerId, Guid RootId)> CreateSutWithRootAsync()
    {
        var db = TestDbContext.Create();
        var ownerId = Guid.NewGuid();
        var root = new Folder
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = Folder.RootName,
            IsRoot = true,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Folders.Add(root);
        await db.SaveChangesAsync();

        var service = new FolderService(db, new FakeClock());
        return (service, db, ownerId, root.Id);
    }

    [Fact]
    public async Task CreateFolderAsync_UnderRoot_Succeeds()
    {
        var (service, db, ownerId, rootId) = await CreateSutWithRootAsync();
        var folderId = Guid.NewGuid();

        var result = await service.CreateFolderAsync(ownerId, folderId, rootId, "Travail");

        Assert.True(result.Succeeded);
        var created = await db.Folders.SingleAsync(f => f.Id == folderId);
        Assert.Equal("Travail", created.Name);
        Assert.Equal(rootId, created.ParentId);
    }

    [Fact]
    public async Task CreateFolderAsync_WithUnknownParent_ReturnsParentNotFound()
    {
        var (service, _, ownerId, _) = await CreateSutWithRootAsync();

        var result = await service.CreateFolderAsync(ownerId, Guid.NewGuid(), Guid.NewGuid(), "Travail");

        Assert.False(result.Succeeded);
        Assert.Equal(FolderError.ParentNotFound, result.Error);
    }

    [Fact]
    public async Task CreateFolderAsync_WithParentOwnedByAnotherUser_ReturnsParentNotFound()
    {
        var (service, db, _, _) = await CreateSutWithRootAsync();
        var otherOwnerId = Guid.NewGuid();

        var result = await service.CreateFolderAsync(otherOwnerId, Guid.NewGuid(), (await db.Folders.SingleAsync()).Id, "Hack");

        Assert.False(result.Succeeded);
        Assert.Equal(FolderError.ParentNotFound, result.Error);
    }

    [Fact]
    public async Task RenameFolderAsync_OnRoot_ReturnsCannotModifyRoot()
    {
        var (service, _, ownerId, rootId) = await CreateSutWithRootAsync();

        var result = await service.RenameFolderAsync(ownerId, rootId, "Nouveau nom");

        Assert.False(result.Succeeded);
        Assert.Equal(FolderError.CannotModifyRoot, result.Error);
    }

    [Fact]
    public async Task DeleteFolderAsync_OnRoot_ReturnsCannotModifyRoot()
    {
        var (service, _, ownerId, rootId) = await CreateSutWithRootAsync();

        var result = await service.DeleteFolderAsync(ownerId, rootId);

        Assert.False(result.Succeeded);
        Assert.Equal(FolderError.CannotModifyRoot, result.Error);
    }

    [Fact]
    public async Task DeleteFolderAsync_CascadesToDescendantFoldersAndEntries()
    {
        var (service, db, ownerId, rootId) = await CreateSutWithRootAsync();

        var parentResult = await service.CreateFolderAsync(ownerId, Guid.NewGuid(), rootId, "Parent");
        var parent = parentResult.Folder!;
        var childResult = await service.CreateFolderAsync(ownerId, Guid.NewGuid(), parent.Id, "Enfant");
        var child = childResult.Folder!;

        db.Entries.Add(new Entry
        {
            Id = Guid.NewGuid(),
            FolderId = child.Id,
            OwnerId = ownerId,
            Title = "Secret",
            Password = "pwd",
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var deleteResult = await service.DeleteFolderAsync(ownerId, parent.Id);

        Assert.True(deleteResult.Succeeded);
        var remaining = await service.GetFoldersAsync(ownerId);
        Assert.Single(remaining); // only root left
        Assert.True((await db.Folders.SingleAsync(f => f.Id == parent.Id)).DeletedAt.HasValue);
        Assert.True((await db.Folders.SingleAsync(f => f.Id == child.Id)).DeletedAt.HasValue);
        Assert.True((await db.Entries.SingleAsync()).DeletedAt.HasValue);
    }
}
