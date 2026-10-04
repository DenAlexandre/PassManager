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
    public const string LastLoginEmail = "last_login_email";

    // Cached from the most recent successful online login, kept across logout so a later
    // connection attempt can unlock the local vault file even if the server is unreachable.
    public const string CachedUserId = "cached_user_id";
    public const string CachedVaultSalt = "cached_vault_salt";
    public const string CachedArgon2Params = "cached_argon2_params";
    public static string AccessToken(Guid userId) => $"access_token_{userId}";
    public static string RefreshToken(Guid userId) => $"refresh_token_{userId}";
}
