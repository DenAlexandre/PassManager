namespace PassManager.Core.Auth;

public interface IAuthApiClient
{
    Task RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default);
    Task ResendConfirmationAsync(ResendConfirmationRequest request, CancellationToken ct = default);
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<RefreshResponse> RefreshAsync(string refreshToken, string? deviceId, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
}
