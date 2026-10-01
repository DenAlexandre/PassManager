using PassManager.Core.Models;
using PassManager.Core.Vault;

namespace PassManager.Core.Sync;

/// <summary>
/// Reconciles the local vault with the server: pushes locally dirty folders/entries/tombstones, then pulls
/// from the pre-push cursor so the just-pushed items (and anything changed concurrently, e.g. a share) come
/// back with their authoritative server version/timestamp in one pass.
/// </summary>
public class SyncEngine(ISyncApiClient apiClient)
{
    public async Task SyncAsync(VaultRepository vault, CancellationToken ct = default)
    {
        var lastSync = vault.Document.SyncState.LastSyncUtc;

        var folderPushItems = BuildFolderPushItems(vault, lastSync);
        var entryPushItems = BuildEntryPushItems(vault, lastSync);

        if (folderPushItems.Count > 0 || entryPushItems.Count > 0)
        {
            await apiClient.PushAsync(folderPushItems, entryPushItems, ct);
        }

        var cursor = lastSync;
        PullResponse pull;
        do
        {
            pull = await apiClient.PullAsync(cursor, ct);

            foreach (var folder in pull.Folders)
            {
                vault.ApplyPulledFolder(folder);
            }

            foreach (var entry in pull.Entries)
            {
                vault.ApplyPulledEntry(entry);
            }

            cursor = pull.NextCursor;
        } while (pull.HasMore);

        vault.Document.SyncState.LastSyncUtc = cursor;
    }

    private static List<FolderPushItem> BuildFolderPushItems(VaultRepository vault, DateTimeOffset? since)
    {
        var items = vault.GetFoldersModifiedSince(since)
            .Where(f => !f.IsRoot)
            .Select(f => new FolderPushItem(f.Id, f.ParentId, f.Name, f.Version, f.UpdatedAt, Deleted: false))
            .ToList();

        foreach (var tombstone in vault.GetTombstonesSince(since).Where(t => t.Type == TombstoneType.Folder && t.Version > 0))
        {
            items.Add(new FolderPushItem(tombstone.Id, null, "", tombstone.Version, tombstone.DeletedAt, Deleted: true));
        }

        return items;
    }

    private static List<EntryPushItem> BuildEntryPushItems(VaultRepository vault, DateTimeOffset? since)
    {
        var items = vault.GetEntriesModifiedSince(since)
            .Select(e => new EntryPushItem(e.Id, e.FolderId, e.Title, e.Url, e.Login, e.Password, e.Memo, e.Version, e.UpdatedAt, Deleted: false))
            .ToList();

        foreach (var tombstone in vault.GetTombstonesSince(since).Where(t => t.Type == TombstoneType.Entry && t.Version > 0))
        {
            items.Add(new EntryPushItem(tombstone.Id, Guid.Empty, "", null, null, "", null, tombstone.Version, tombstone.DeletedAt, Deleted: true));
        }

        return items;
    }
}
