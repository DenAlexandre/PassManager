namespace PassManager.Application.Sync;

public record FolderSyncDto(
    Guid Id, Guid? ParentId, string Name, bool IsRoot, long Version, DateTimeOffset UpdatedAt, bool Deleted);

public record EntrySyncDto(
    Guid Id, Guid FolderId, string Title, string? Url, string? Login, string Password, string? Memo,
    long Version, DateTimeOffset UpdatedAt, bool Deleted);

public record PullResult(
    DateTimeOffset ServerTime,
    List<FolderSyncDto> Folders,
    List<EntrySyncDto> Entries,
    DateTimeOffset NextCursor,
    bool HasMore);

public record FolderPushItem(
    Guid Id, Guid? ParentId, string Name, long ClientVersion, DateTimeOffset UpdatedAt, bool Deleted);

public record EntryPushItem(
    Guid Id, Guid FolderId, string Title, string? Url, string? Login, string Password, string? Memo,
    long ClientVersion, DateTimeOffset UpdatedAt, bool Deleted);

public record PushConflict(Guid Id, string Type, FolderSyncDto? ServerFolder, EntrySyncDto? ServerEntry);

public record PushRejected(Guid Id, string Type, string Reason);

public record PushResult(List<Guid> Accepted, List<PushConflict> Conflicts, List<PushRejected> Rejected);
