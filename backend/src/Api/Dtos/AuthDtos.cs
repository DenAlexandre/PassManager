using System.ComponentModel.DataAnnotations;

namespace PassManager.Api.Dtos;

public record RegisterRequestDto(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password);

public record ConfirmEmailRequestDto(
    [Required, EmailAddress] string Email,
    [Required] string Token);

public record ResendConfirmationRequestDto(
    [Required, EmailAddress] string Email);

public record LoginRequestDto(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    string? DeviceId);

public record RefreshRequestDto(
    [Required] string RefreshToken,
    string? DeviceId);

public record LogoutRequestDto(
    [Required] string RefreshToken);

public record Argon2ParamsDto(int MemoryKiB, int Iterations, int Parallelism);

public record LoginResponseDto(
    Guid UserId,
    string AccessToken,
    string RefreshToken,
    string VaultSalt,
    Argon2ParamsDto Argon2Params);

public record RefreshResponseDto(string AccessToken, string RefreshToken);

public record ErrorResponseDto(string Code, string Message);
