using PassManager.Core.Sync;

namespace PassManager.Core.Tests.TestDoubles;

public class FakeSyncApiClient : ISyncApiClient
{
    public List<FolderSyncItem> ServerFolders { get; } = [];
    public List<EntrySyncItem> ServerEntries { get; } = [];

    public List<FolderPushItem> LastPushedFolders { get; private set; } = [];
    public List<EntryPushItem> LastPushedEntries { get; private set; } = [];
    public int PushCallCount { get; private set; }
    public int PullCallCount { get; private set; }

    public DateTimeOffset ServerTime { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public Task<PullResponse> PullAsync(DateTimeOffset? since, CancellationToken ct = default)
    {
        PullCallCount++;
        var sinceValue = since ?? DateTimeOffset.MinValue;

        var folders = ServerFolders.Where(f => f.UpdatedAt > sinceValue).ToList();
        var entries = ServerEntries.Where(e => e.UpdatedAt > sinceValue).ToList();

        return Task.FromResult(new PullResponse(ServerTime, folders, entries, ServerTime, HasMore: false));
    }

    public Task<PushResponse> PushAsync(List<FolderPushItem> folders, List<EntryPushItem> entries, CancellationToken ct = default)
    {
        PushCallCount++;
        LastPushedFolders = folders;
        LastPushedEntries = entries;

        var accepted = new List<Guid>();
        foreach (var folder in folders)
        {
            ServerFolders.RemoveAll(f => f.Id == folder.Id);
            ServerFolders.Add(new FolderSyncItem(folder.Id, folder.ParentId, folder.Name, false, folder.ClientVersion + 1, ServerTime, folder.Deleted));
            accepted.Add(folder.Id);
        }

        foreach (var entry in entries)
        {
            ServerEntries.RemoveAll(e => e.Id == entry.Id);
            ServerEntries.Add(new EntrySyncItem(entry.Id, entry.FolderId, entry.Title, entry.Url, entry.Login, entry.Password, entry.Memo, entry.ClientVersion + 1, ServerTime, entry.Deleted));
            accepted.Add(entry.Id);
        }

        return Task.FromResult(new PushResponse(accepted, [], []));
    }
}
