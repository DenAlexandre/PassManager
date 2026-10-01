using System.Net;
using System.Net.Http.Headers;
using PassManager.Core.Abstractions;
using PassManager.Core.Auth;
using PassManager.Maui.Common;

namespace PassManager.Maui.Services.Platform;

/// <summary>
/// Attaches the current JWT to outgoing requests and transparently retries once after a refresh on 401.
/// Depends on ISecureStorageService directly (not AuthSessionService) to avoid a DI cycle: AuthSessionService
/// depends on SyncEngine, which depends on ISyncApiClient, whose HttpClient pipeline includes this handler.
/// </summary>
public class AuthHeaderHandler(ISecureStorageService secureStorage, IAuthApiClient authApiClient) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var userId = await GetCurrentUserIdAsync();
        if (userId is not null)
        {
            var accessToken = await secureStorage.GetAsync(SessionStorageKeys.AccessToken(userId.Value));
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized || userId is null)
        {
            return response;
        }

        var refreshToken = await secureStorage.GetAsync(SessionStorageKeys.RefreshToken(userId.Value));
        if (string.IsNullOrEmpty(refreshToken))
        {
            return response;
        }

        try
        {
            var refreshed = await authApiClient.RefreshAsync(refreshToken, DeviceIdProvider.GetOrCreate(), ct);
            await secureStorage.SetAsync(SessionStorageKeys.AccessToken(userId.Value), refreshed.AccessToken);
            await secureStorage.SetAsync(SessionStorageKeys.RefreshToken(userId.Value), refreshed.RefreshToken);

            var retryRequest = await CloneRequestAsync(request);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
            response.Dispose();
            return await base.SendAsync(retryRequest, ct);
        }
        catch (AuthApiException)
        {
            return response;
        }
    }

    private async Task<Guid?> GetCurrentUserIdAsync()
    {
        var raw = await secureStorage.GetAsync(SessionStorageKeys.CurrentUserId);
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
