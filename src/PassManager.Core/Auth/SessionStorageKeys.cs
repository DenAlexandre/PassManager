namespace PassManager.Core.Auth;

/// <summary>
/// Secure-storage key naming shared between AuthSessionService and the HTTP auth header handler.
/// Kept separate so the handler can read/refresh tokens without depending on AuthSessionService itself
/// (which would create a DI cycle: AuthSessionService -> SyncEngine -> ISyncApiClient -> handler -> AuthSessionService).
/// </summary>
public static class SessionStorageKeys
{
    public const string CurrentUserId = "current_user_id";
    public const string CurrentEmail = "current_email";
    public static string AccessToken(Guid userId) => $"access_token_{userId}";
    public static string RefreshToken(Guid userId) => $"refresh_token_{userId}";
}
