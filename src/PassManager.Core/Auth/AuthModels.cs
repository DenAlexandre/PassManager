using PassManager.Core.Crypto;

namespace PassManager.Core.Auth;

public record RegisterRequest(string Email, string Password);

public record LoginRequest(string Email, string Password, string? DeviceId);

public record ConfirmEmailRequest(string Email, string Token);

public record ResendConfirmationRequest(string Email);

public record LoginResponse(Guid UserId, string AccessToken, string RefreshToken, byte[] VaultSalt, Argon2Params Argon2Params);

public record RefreshResponse(string AccessToken, string RefreshToken);

public enum AuthApiErrorCode
{
    Unknown,
    InvalidCredentials,
    EmailNotConfirmed,
    InvalidOrExpiredToken,
    InvalidRefreshToken
}

public class AuthApiException(AuthApiErrorCode code, string message) : Exception(message)
{
    public AuthApiErrorCode Code { get; } = code;
}
