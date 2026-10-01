using Microsoft.EntityFrameworkCore;
using PassManager.Application.Entries;
using PassManager.Application.Folders;
using PassManager.Application.Sharing;
using PassManager.Application.Tests.TestDoubles;
using PassManager.Domain.Entities;
using Xunit;

namespace PassManager.Application.Tests.Sharing;

public class EntrySharingServiceTests
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
    public async Task ShareAsync_EntryTwoLevelsDeep_RecreatesChainAndCopiesEntry()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);

        var folderService = new FolderService(db, clock);
        var entryService = new EntryService(db, clock);
        var dev = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), aliceRoot, "Dev")).Folder!;
        var outils = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), dev.Id, "Outils")).Folder!;
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), outils.Id, "GitHub", "https://github.com", "alice", "secret", "memo")).Entry!;

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);

        Assert.True(result.Succeeded);

        var bobFolders = await folderService.GetFoldersAsync(bob);
        Assert.Equal(3, bobFolders.Count); // root + copied Dev + copied Outils
        var copiedDev = bobFolders.Single(f => f.Name == "Dev");
        var bobRoot = await GetRootIdAsync(db, bob);
        Assert.Equal(bobRoot, copiedDev.ParentId);

        var copiedOutils = bobFolders.Single(f => f.Name == "Outils");
        Assert.Equal(copiedDev.Id, copiedOutils.ParentId);

        var bobEntries = await entryService.GetEntriesAsync(bob, copiedOutils.Id);
        var copiedEntry = Assert.Single(bobEntries);
        Assert.Equal("secret", copiedEntry.Password);
        Assert.Equal(result.NewEntryId, copiedEntry.Id);

        // original untouched
        var aliceEntries = await entryService.GetEntriesAsync(alice, outils.Id);
        Assert.Single(aliceEntries);
    }

    [Fact]
    public async Task ShareAsync_EntryDirectlyInRoot_CopiesToTargetRootWithNoFolderCreated()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var bobRoot = await GetRootIdAsync(db, bob);

        var entryService = new EntryService(db, clock);
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), aliceRoot, "GitHub", null, "alice", "secret", null)).Entry!;

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);

        Assert.True(result.Succeeded);

        var folderService = new FolderService(db, clock);
        var bobFolders = await folderService.GetFoldersAsync(bob);
        Assert.Single(bobFolders); // root only, no folder created

        var bobEntries = await entryService.GetEntriesAsync(bob, bobRoot);
        var copiedEntry = Assert.Single(bobEntries);
        Assert.Equal("GitHub", copiedEntry.Title);
    }

    [Fact]
    public async Task ShareAsync_NameCollisionOnFirstAncestor_OnlySuffixesFirstFolder()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var bobRoot = await GetRootIdAsync(db, bob);

        var folderService = new FolderService(db, clock);
        var entryService = new EntryService(db, clock);
        var dev = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), aliceRoot, "Dev")).Folder!;
        var outils = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), dev.Id, "Outils")).Folder!;
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), outils.Id, "GitHub", null, "alice", "secret", null)).Entry!;

        // Bob already has top-level folders named "Dev" and "Outils" (collision at both levels)
        var bobDev = (await folderService.CreateFolderAsync(bob, Guid.NewGuid(), bobRoot, "Dev")).Folder!;
        await folderService.CreateFolderAsync(bob, Guid.NewGuid(), bobDev.Id, "Outils");

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);

        Assert.True(result.Succeeded);
        var bobFolders = await folderService.GetFoldersAsync(bob);
        Assert.Contains(bobFolders, f => f.Name == "Dev (partagé par alice@example.com)");
        // the deeper "Outils" is a sibling duplicate of Bob's own "Outils" under the ORIGINAL "Dev",
        // but under the NEWLY created "Dev (partagé par ...)" it must keep the plain name "Outils"
        var newDev = bobFolders.Single(f => f.Name == "Dev (partagé par alice@example.com)");
        var outilsUnderNewDev = bobFolders.Single(f => f.ParentId == newDev.Id);
        Assert.Equal("Outils", outilsUnderNewDev.Name);
    }

    [Fact]
    public async Task ShareAsync_SameEntrySharedTwice_CreatesTwoIndependentCopies()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);

        var folderService = new FolderService(db, clock);
        var entryService = new EntryService(db, clock);
        var dev = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), aliceRoot, "Dev")).Folder!;
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), dev.Id, "GitHub", null, "alice", "secret", null)).Entry!;

        var sharingService = new EntrySharingService(db, clock);
        var first = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);
        var second = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.NewEntryId, second.NewEntryId);

        var bobFolders = await folderService.GetFoldersAsync(bob);
        // Two independent chains were created (no merge) — but the second "Dev" collides by name with the
        // first share's own copy, so the existing collision-suffix rule correctly renames it, same as any
        // other top-level name collision.
        Assert.Contains(bobFolders, f => f.Name == "Dev");
        Assert.Contains(bobFolders, f => f.Name == "Dev (partagé par alice@example.com)");
    }

    [Fact]
    public async Task ShareAsync_DeletedEntry_IsTreatedAsNotFound()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);

        var entryService = new EntryService(db, clock);
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), aliceRoot, "GitHub", null, "alice", "secret", null)).Entry!;
        await entryService.DeleteEntryAsync(alice, entry.Id);

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(EntryShareError.EntryNotFound, result.Error);
    }

    [Fact]
    public async Task ShareAsync_ToSelf_IsRejected()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var entryService = new EntryService(db, clock);
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), aliceRoot, "GitHub", null, "alice", "secret", null)).Entry!;

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, alice, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(EntryShareError.CannotShareToSelf, result.Error);
    }

    [Fact]
    public async Task ShareAsync_ToUnknownTarget_IsRejected()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var entryService = new EntryService(db, clock);
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), aliceRoot, "GitHub", null, "alice", "secret", null)).Entry!;

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(EntryShareError.TargetUserNotFound, result.Error);
    }
}
