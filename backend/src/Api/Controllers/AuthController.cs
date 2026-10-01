using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PassManager.Api.Dtos;
using PassManager.Application.Auth;
using PassManager.Domain.Security;

namespace PassManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequestDto request, CancellationToken ct)
    {
        await authService.RegisterAsync(request.Email, request.Password, ct);
        return Accepted(new { message = "Si cet email n'est pas déjà utilisé, un lien de confirmation vient de vous être envoyé." });
    }

    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequestDto request, CancellationToken ct)
    {
        var confirmed = await authService.ConfirmEmailAsync(request.Email, request.Token, ct);
        if (!confirmed)
        {
            return BadRequest(new ErrorResponseDto("INVALID_OR_EXPIRED_TOKEN", "Le lien de confirmation est invalide ou a expiré."));
        }

        return Ok(new { message = "Compte confirmé, vous pouvez vous connecter." });
    }

    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationRequestDto request, CancellationToken ct)
    {
        await authService.ResendConfirmationAsync(request.Email, ct);
        return Accepted(new { message = "Si ce compte existe et n'est pas confirmé, un nouveau lien vient d'être envoyé." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequestDto request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request.Email, request.Password, request.DeviceId, ct);

        if (!result.Succeeded)
        {
            return result.Error switch
            {
                AuthError.EmailNotConfirmed => StatusCode(StatusCodes.Status403Forbidden,
                    new ErrorResponseDto("EMAIL_NOT_CONFIRMED", "Veuillez confirmer votre email avant de vous connecter.")),
                _ => Unauthorized(new ErrorResponseDto("INVALID_CREDENTIALS", "Email ou mot de passe incorrect."))
            };
        }

        return Ok(new LoginResponseDto(
            result.UserId,
            result.AccessToken!,
            result.RefreshToken!,
            Convert.ToBase64String(result.VaultSalt!),
            new Argon2ParamsDto(Argon2Defaults.MemoryKiB, Argon2Defaults.Iterations, Argon2Defaults.Parallelism)));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequestDto request, CancellationToken ct)
    {
        var result = await authService.RefreshAsync(request.RefreshToken, request.DeviceId, ct);
        if (!result.Succeeded)
        {
            return Unauthorized(new ErrorResponseDto("INVALID_REFRESH_TOKEN", "Session expirée, merci de vous reconnecter."));
        }

        return Ok(new RefreshResponseDto(result.AccessToken!, result.RefreshToken!));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequestDto request, CancellationToken ct)
    {
        await authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }
}
