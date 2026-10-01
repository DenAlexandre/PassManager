namespace PassManager.Application.Auth;

public enum AuthError
{
    None,
    EmailAlreadyRegistered,
    InvalidCredentials,
    EmailNotConfirmed,
    InvalidOrExpiredToken,
    InvalidRefreshToken
}

public record RegisterResult(bool Succeeded, AuthError Error);

public record LoginResult(
    bool Succeeded,
    AuthError Error,
    Guid UserId,
    string? AccessToken,
    string? RefreshToken,
    byte[]? VaultSalt);

public record RefreshResult(
    bool Succeeded,
    AuthError Error,
    string? AccessToken,
    string? RefreshToken);
