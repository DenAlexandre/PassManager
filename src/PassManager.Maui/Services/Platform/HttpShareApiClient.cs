using System.Net.Http.Json;
using System.Text.Json;
using PassManager.Core.Sharing;

namespace PassManager.Maui.Services.Platform;

public class HttpShareApiClient(HttpClient httpClient) : IShareApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<List<UserSummary>> SearchUsersAsync(string query, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"api/users/search?query={Uri.EscapeDataString(query)}", ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<UserSummary>>(JsonOptions, ct) ?? [];
    }

    public async Task<Guid> ShareFolderAsync(Guid folderId, Guid targetUserId, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync($"api/folders/{folderId}/share", new { targetUserId }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ShareResponsePayload>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Réponse de partage invalide.");
        return payload.NewFolderId;
    }

    private record ShareResponsePayload(Guid NewFolderId);
}
