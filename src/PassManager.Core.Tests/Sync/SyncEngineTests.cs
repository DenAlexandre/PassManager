using PassManager.Core.Models;
using PassManager.Core.Sync;
using PassManager.Core.Tests.TestDoubles;
using PassManager.Core.Vault;
using Xunit;

namespace PassManager.Core.Tests.Sync;

public class SyncEngineTests
{
    private static VaultRepository CreateEmptyVault(FakeClock clock) =>
        new(VaultDocument.CreateEmpty(), clock);

    [Fact]
    public async Task SyncAsync_PullsServerFoldersIntoEmptyLocalVault()
    {
        var clock = new FakeClock();
        var vault = CreateEmptyVault(clock);
        var apiClient = new FakeSyncApiClient();
        var rootId = Guid.NewGuid();
        apiClient.ServerFolders.Add(new FolderSyncItem(rootId, null, VaultFolder.RootName, IsRoot: true, Version: 1, UpdatedAt: clock.UtcNow, Deleted: false));

        var engine = new SyncEngine(apiClient);
        await engine.SyncAsync(vault);

        var root = Assert.Single(vault.GetFolders());
        Assert.Equal(rootId, root.Id);
        Assert.True(root.IsRoot);
        Assert.Equal(apiClient.ServerTime, vault.Document.SyncState.LastSyncUtc);
    }

    [Fact]
    public async Task SyncAsync_PushesLocallyCreatedFolder_AndUpdatesItWithServerVersion()
    {
        var clock = new FakeClock();
        var vault = CreateEmptyVault(clock);
        var apiClient = new FakeSyncApiClient();
        var rootId = Guid.NewGuid();
        apiClient.ServerFolders.Add(new FolderSyncItem(rootId, null, VaultFolder.RootName, true, 1, clock.UtcNow, false));
        var engine = new SyncEngine(apiClient);
        await engine.SyncAsync(vault); // first sync: discover root

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var localFolder = vault.CreateFolder(rootId, "Travail");
        Assert.Equal(0, localFolder.Version);

        apiClient.ServerTime = clock.UtcNow;
        await engine.SyncAsync(vault);

        Assert.Equal(1, apiClient.PushCallCount);
        Assert.Contains(apiClient.LastPushedFolders, f => f.Id == localFolder.Id);

        var syncedFolder = vault.GetFolders().Single(f => f.Id == localFolder.Id);
        Assert.Equal(1, syncedFolder.Version); // server-assigned version came back via the post-push pull
    }

    [Fact]
    public async Task SyncAsync_NeverSyncedDeletedFolder_IsNotPushed()
    {
        var clock = new FakeClock();
        var vault = CreateEmptyVault(clock);
        var apiClient = new FakeSyncApiClient();
        var rootId = Guid.NewGuid();
        apiClient.ServerFolders.Add(new FolderSyncItem(rootId, null, VaultFolder.RootName, true, 1, clock.UtcNow, false));
        var engine = new SyncEngine(apiClient);
        await engine.SyncAsync(vault);

        var localFolder = vault.CreateFolder(rootId, "Éphémère");
        vault.DeleteFolder(localFolder.Id);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        apiClient.ServerTime = clock.UtcNow;
        await engine.SyncAsync(vault);

        Assert.DoesNotContain(apiClient.LastPushedFolders, f => f.Id == localFolder.Id);
    }

    [Fact]
    public async Task SyncAsync_PushesDeleteOfPreviouslySyncedFolder()
    {
        var clock = new FakeClock();
        var vault = CreateEmptyVault(clock);
        var apiClient = new FakeSyncApiClient();
        var rootId = Guid.NewGuid();
        apiClient.ServerFolders.Add(new FolderSyncItem(rootId, null, VaultFolder.RootName, true, 1, clock.UtcNow, false));
        var engine = new SyncEngine(apiClient);
        await engine.SyncAsync(vault);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        var localFolder = vault.CreateFolder(rootId, "Travail");
        apiClient.ServerTime = clock.UtcNow;
        await engine.SyncAsync(vault); // now synced with Version=1

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        vault.DeleteFolder(localFolder.Id);
        apiClient.ServerTime = clock.UtcNow;
        await engine.SyncAsync(vault);

        Assert.Contains(apiClient.LastPushedFolders, f => f.Id == localFolder.Id && f.Deleted);
        Assert.DoesNotContain(vault.GetFolders(), f => f.Id == localFolder.Id);
    }
}
