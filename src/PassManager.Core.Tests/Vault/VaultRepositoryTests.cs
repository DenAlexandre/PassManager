using PassManager.Core.Models;
using PassManager.Core.Tests.TestDoubles;
using PassManager.Core.Vault;
using Xunit;

namespace PassManager.Core.Tests.Vault;

public class VaultRepositoryTests
{
    private static (VaultRepository Repository, FakeClock Clock, Guid RootId) CreateSutWithRoot()
    {
        var document = VaultDocument.CreateEmpty();
        var clock = new FakeClock();
        var root = new VaultFolder { Id = Guid.NewGuid(), Name = VaultFolder.RootName, IsRoot = true, Version = 1, UpdatedAt = clock.UtcNow };
        document.Folders.Add(root);

        return (new VaultRepository(document, clock), clock, root.Id);
    }

    [Fact]
    public void CreateFolder_UnderRoot_AddsFolder()
    {
        var (repo, _, rootId) = CreateSutWithRoot();

        var folder = repo.CreateFolder(rootId, "Travail");

        Assert.Equal(rootId, folder.ParentId);
        Assert.Contains(folder, repo.GetChildFolders(rootId));
    }

    [Fact]
    public void RenameFolder_OnRoot_Throws()
    {
        var (repo, _, rootId) = CreateSutWithRoot();

        Assert.Throws<InvalidOperationException>(() => repo.RenameFolder(rootId, "Nope"));
    }

    [Fact]
    public void DeleteFolder_OnRoot_Throws()
    {
        var (repo, _, rootId) = CreateSutWithRoot();

        Assert.Throws<InvalidOperationException>(() => repo.DeleteFolder(rootId));
    }

    [Fact]
    public void DeleteFolder_CascadesToDescendantFoldersAndEntries_AndRecordsTombstones()
    {
        var (repo, _, rootId) = CreateSutWithRoot();
        var parent = repo.CreateFolder(rootId, "Parent");
        var child = repo.CreateFolder(parent.Id, "Enfant");
        var entry = repo.CreateEntry(child.Id, "Secret", null, null, "pwd", null);

        repo.DeleteFolder(parent.Id);

        Assert.DoesNotContain(parent, repo.GetFolders());
        Assert.DoesNotContain(child, repo.GetFolders());
        Assert.Empty(repo.GetEntries(child.Id));

        var tombstoneIds = repo.GetTombstonesSince(null).Select(t => t.Id).ToList();
        Assert.Contains(parent.Id, tombstoneIds);
        Assert.Contains(child.Id, tombstoneIds);
        Assert.Contains(entry.Id, tombstoneIds);
    }

    [Fact]
    public void CreateEntry_ThenUpdate_ChangesFieldsAndBumpsUpdatedAt()
    {
        var (repo, clock, rootId) = CreateSutWithRoot();
        var entry = repo.CreateEntry(rootId, "Old", null, null, "old-pwd", null);

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        repo.UpdateEntry(entry.Id, "New", "https://example.com", "login", "new-pwd", "memo");

        var updated = repo.GetEntries(rootId).Single(e => e.Id == entry.Id);
        Assert.Equal("New", updated.Title);
        Assert.Equal("new-pwd", updated.Password);
        Assert.Equal(clock.UtcNow, updated.UpdatedAt);
    }

    [Fact]
    public void DeleteEntry_RemovesItAndRecordsTombstone()
    {
        var (repo, _, rootId) = CreateSutWithRoot();
        var entry = repo.CreateEntry(rootId, "ToDelete", null, null, "pwd", null);

        repo.DeleteEntry(entry.Id);

        Assert.Empty(repo.GetEntries(rootId));
        Assert.Contains(repo.GetTombstonesSince(null), t => t.Id == entry.Id && t.Type == TombstoneType.Entry);
    }

    [Fact]
    public void GetFoldersModifiedSince_OnlyReturnsFoldersNewerThanCursor()
    {
        var (repo, clock, rootId) = CreateSutWithRoot();
        var cursor = clock.UtcNow;

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var newFolder = repo.CreateFolder(rootId, "Nouveau");

        var modified = repo.GetFoldersModifiedSince(cursor);

        Assert.Single(modified);
        Assert.Equal(newFolder.Id, modified[0].Id);
    }
}
