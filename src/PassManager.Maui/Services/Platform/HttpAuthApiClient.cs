using System.Net.Http.Json;
using System.Text.Json;
using PassManager.Core.Auth;
using PassManager.Core.Crypto;

namespace PassManager.Maui.Services.Platform;

public class HttpAuthApiClient(HttpClient httpClient) : IAuthApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/auth/register", new { email = request.Email, password = request.Password }, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/auth/confirm-email", new { email = request.Email, token = request.Token }, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task ResendConfirmationAsync(ResendConfirmationRequest request, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/auth/resend-confirmation", new { email = request.Email }, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/auth/login",
            new { email = request.Email, password = request.Password, deviceId = request.DeviceId },
            JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);

        var payload = await response.Content.ReadFromJsonAsync<LoginPayload>(JsonOptions, ct)
            ?? throw new AuthApiException(AuthApiErrorCode.Unknown, "Réponse de connexion invalide.");

        return new LoginResponse(
            payload.UserId,
            payload.AccessToken,
            payload.RefreshToken,
            Convert.FromBase64String(payload.VaultSalt),
            new Argon2Params(payload.Argon2Params.MemoryKiB, payload.Argon2Params.Iterations, payload.Argon2Params.Parallelism));
    }

    public async Task<RefreshResponse> RefreshAsync(string refreshToken, string? deviceId, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/auth/refresh", new { refreshToken, deviceId }, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);

        var payload = await response.Content.ReadFromJsonAsync<RefreshPayload>(JsonOptions, ct)
            ?? throw new AuthApiException(AuthApiErrorCode.Unknown, "Réponse de rafraîchissement invalide.");

        return new RefreshResponse(payload.AccessToken, payload.RefreshToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/auth/logout", new { refreshToken }, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var code = AuthApiErrorCode.Unknown;
        var message = "Une erreur est survenue. Merci de réessayer.";

        try
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorPayload>(JsonOptions, ct);
            if (error is not null)
            {
                message = error.Message;
                code = error.Code switch
                {
                    "INVALID_CREDENTIALS" => AuthApiErrorCode.InvalidCredentials,
                    "EMAIL_NOT_CONFIRMED" => AuthApiErrorCode.EmailNotConfirmed,
                    "INVALID_OR_EXPIRED_TOKEN" => AuthApiErrorCode.InvalidOrExpiredToken,
                    "INVALID_REFRESH_TOKEN" => AuthApiErrorCode.InvalidRefreshToken,
                    _ => AuthApiErrorCode.Unknown
                };
            }
        }
        catch (JsonException)
        {
            // best-effort parse of the error payload; fall back to the generic message above
        }

        throw new AuthApiException(code, message);
    }

    private record Argon2ParamsPayload(int MemoryKiB, int Iterations, int Parallelism);

    private record LoginPayload(Guid UserId, string AccessToken, string RefreshToken, string VaultSalt, Argon2ParamsPayload Argon2Params);

    private record RefreshPayload(string AccessToken, string RefreshToken);

    private record ErrorPayload(string Code, string Message);
}
