namespace PassManager.Core.Sync;

public record FolderSyncItem(
    Guid Id, Guid? ParentId, string Name, bool IsRoot, long Version, DateTimeOffset UpdatedAt, bool Deleted);

public record EntrySyncItem(
    Guid Id, Guid FolderId, string Title, string? Url, string? Login, string Password, string? Memo,
    long Version, DateTimeOffset UpdatedAt, bool Deleted);

public record PullResponse(
    DateTimeOffset ServerTime,
    List<FolderSyncItem> Folders,
    List<EntrySyncItem> Entries,
    DateTimeOffset NextCursor,
    bool HasMore);

public record FolderPushItem(
    Guid Id, Guid? ParentId, string Name, long ClientVersion, DateTimeOffset UpdatedAt, bool Deleted);

public record EntryPushItem(
    Guid Id, Guid FolderId, string Title, string? Url, string? Login, string Password, string? Memo,
    long ClientVersion, DateTimeOffset UpdatedAt, bool Deleted);

public record PushConflict(Guid Id, string Type, FolderSyncItem? ServerFolder, EntrySyncItem? ServerEntry);

public record PushRejected(Guid Id, string Type, string Reason);

public record PushResponse(List<Guid> Accepted, List<PushConflict> Conflicts, List<PushRejected> Rejected);
