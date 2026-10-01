namespace PassManager.Core.Sharing;

public record UserSummary(Guid Id, string Email);

public interface IShareApiClient
{
    Task<List<UserSummary>> SearchUsersAsync(string query, CancellationToken ct = default);

    Task<Guid> ShareFolderAsync(Guid folderId, Guid targetUserId, CancellationToken ct = default);
}
