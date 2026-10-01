using Microsoft.EntityFrameworkCore;
using PassManager.Application.Entries;
using PassManager.Application.Folders;
using PassManager.Application.Sharing;
using PassManager.Application.Tests.TestDoubles;
using PassManager.Domain.Entities;
using Xunit;

namespace PassManager.Application.Tests.Sharing;

public class FolderSharingServiceTests
{
    private static async Task<Guid> CreateUserWithRootAsync(TestDbContext db, string email)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Email = email,
            PasswordHash = "x",
            VaultSalt = [1, 2, 3],
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Folders.Add(new Folder
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            Name = Folder.RootName,
            IsRoot = true,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static Task<Guid> GetRootIdAsync(TestDbContext db, Guid ownerId) =>
        db.Folders.Where(f => f.OwnerId == ownerId && f.IsRoot).Select(f => f.Id).SingleAsync();

    [Fact]
    public async Task ShareAsync_CopiesFolderSubtreeAndEntriesIntoTargetRoot()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);

        var folderService = new FolderService(db, clock);
        var entryService = new EntryService(db, clock);
        var parent = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), aliceRoot, "Travail")).Folder!;
        var child = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), parent.Id, "Sous-dossier")).Folder!;
        await entryService.CreateEntryAsync(alice, Guid.NewGuid(), child.Id, "GitHub", "https://github.com", "alice", "secret", "memo");

        var sharingService = new FolderSharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, parent.Id, bob, CancellationToken.None);

        Assert.True(result.Succeeded);

        var bobFolders = await folderService.GetFoldersAsync(bob);
        Assert.Equal(3, bobFolders.Count); // root + copied parent + copied child
        var copiedParent = bobFolders.Single(f => f.Name == "Travail");
        var bobRoot = await GetRootIdAsync(db, bob);
        Assert.Equal(bobRoot, copiedParent.ParentId);

        var copiedChild = bobFolders.Single(f => f.Name == "Sous-dossier");
        Assert.Equal(copiedParent.Id, copiedChild.ParentId);

        var bobEntries = await entryService.GetEntriesAsync(bob, copiedChild.Id);
        var copiedEntry = Assert.Single(bobEntries);
        Assert.Equal("secret", copiedEntry.Password);

        // original untouched
        var aliceEntries = await entryService.GetEntriesAsync(alice, child.Id);
        Assert.Single(aliceEntries);
    }

    [Fact]
    public async Task ShareAsync_WithNameCollisionInTargetRoot_AppendsSharedBySuffix()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var bobRoot = await GetRootIdAsync(db, bob);

        var folderService = new FolderService(db, clock);
        var toShare = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), aliceRoot, "Travail")).Folder!;
        await folderService.CreateFolderAsync(bob, Guid.NewGuid(), bobRoot, "Travail"); // collision

        var sharingService = new FolderSharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, toShare.Id, bob, CancellationToken.None);

        Assert.True(result.Succeeded);
        var bobFolders = await folderService.GetFoldersAsync(bob);
        Assert.Contains(bobFolders, f => f.Name == "Travail (partagé par alice@example.com)");
    }

    [Fact]
    public async Task ShareAsync_RootFolder_IsRejected()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);

        var sharingService = new FolderSharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, aliceRoot, bob, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShareError.CannotShareRoot, result.Error);
    }

    [Fact]
    public async Task ShareAsync_ToSelf_IsRejected()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var folderService = new FolderService(db, clock);
        var folder = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), aliceRoot, "Travail")).Folder!;

        var sharingService = new FolderSharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, folder.Id, alice, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShareError.CannotShareToSelf, result.Error);
    }

    [Fact]
    public async Task ShareAsync_ToUnknownTarget_IsRejected()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var folderService = new FolderService(db, clock);
        var folder = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), aliceRoot, "Travail")).Folder!;

        var sharingService = new FolderSharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, folder.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ShareError.TargetUserNotFound, result.Error);
    }

    [Fact]
    public async Task SearchUsersAsync_ExcludesSelfAndRespectsMinLength()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        await CreateUserWithRootAsync(db, "alicia@example.com");
        await CreateUserWithRootAsync(db, "bob@example.com");

        var sharingService = new FolderSharingService(db, clock);

        var tooShort = await sharingService.SearchUsersAsync(alice, "a");
        Assert.Empty(tooShort);

        var results = await sharingService.SearchUsersAsync(alice, "ali");
        Assert.Single(results);
        Assert.Equal("alicia@example.com", results[0].Email);
    }
}
