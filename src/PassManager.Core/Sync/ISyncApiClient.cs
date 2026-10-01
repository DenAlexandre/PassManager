namespace PassManager.Core.Sync;

public interface ISyncApiClient
{
    Task<PullResponse> PullAsync(DateTimeOffset? since, CancellationToken ct = default);

    Task<PushResponse> PushAsync(List<FolderPushItem> folders, List<EntryPushItem> entries, CancellationToken ct = default);
}
