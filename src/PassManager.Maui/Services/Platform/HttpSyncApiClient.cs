using System.Net.Http.Json;
using System.Text.Json;
using PassManager.Core.Sync;

namespace PassManager.Maui.Services.Platform;

public class HttpSyncApiClient(HttpClient httpClient) : ISyncApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PullResponse> PullAsync(DateTimeOffset? since, CancellationToken ct = default)
    {
        var url = since is null ? "api/sync/pull" : $"api/sync/pull?since={Uri.EscapeDataString(since.Value.ToString("O"))}";
        var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PullResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Réponse de synchronisation (pull) invalide.");
    }

    public async Task<PushResponse> PushAsync(List<FolderPushItem> folders, List<EntryPushItem> entries, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("api/sync/push", new { folders, entries }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PushResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Réponse de synchronisation (push) invalide.");
    }
}
