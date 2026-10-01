using PassManager.Core.Abstractions;
using PassManager.Core.Crypto;
using PassManager.Core.Sync;
using PassManager.Core.Vault;

namespace PassManager.Core.Auth;

/// <summary>
/// Orchestrates login/registration against the API and the local encrypted vault: derives the vault key from the
/// account password (never sent to the server), loads/creates the on-device .pmvault file, and performs the
/// initial sync so the vault reflects server state right after login.
/// </summary>
public class AuthSessionService(
    IAuthApiClient apiClient,
    IVaultFileStore vaultFileStore,
    ISecureStorageService secureStorage,
    VaultCryptoService vaultCrypto,
    SyncEngine syncEngine,
    IClock clock)
{
    private byte[]? _vaultKey;
    private byte[]? _vaultSalt;
    private Argon2Params? _argon2Params;

    public Guid? CurrentUserId { get; private set; }
    public string? CurrentEmail { get; private set; }
    public VaultRepository? Vault { get; private set; }

    public Task RegisterAsync(string email, string password, CancellationToken ct = default) =>
        apiClient.RegisterAsync(new RegisterRequest(email, password), ct);

    public Task ConfirmEmailAsync(string email, string token, CancellationToken ct = default) =>
        apiClient.ConfirmEmailAsync(new ConfirmEmailRequest(email, token), ct);

    public Task ResendConfirmationAsync(string email, CancellationToken ct = default) =>
        apiClient.ResendConfirmationAsync(new ResendConfirmationRequest(email), ct);

    public async Task LoginAsync(string email, string password, string deviceId, CancellationToken ct = default)
    {
        var response = await apiClient.LoginAsync(new LoginRequest(email, password, deviceId), ct);

        await secureStorage.SetAsync(SessionStorageKeys.AccessToken(response.UserId), response.AccessToken);
        await secureStorage.SetAsync(SessionStorageKeys.RefreshToken(response.UserId), response.RefreshToken);
        await secureStorage.SetAsync(SessionStorageKeys.CurrentUserId, response.UserId.ToString());
        await secureStorage.SetAsync(SessionStorageKeys.CurrentEmail, email);

        _vaultSalt = response.VaultSalt;
        _argon2Params = response.Argon2Params;
        _vaultKey = vaultCrypto.DeriveKey(password, _vaultSalt, _argon2Params);

        VaultDocument document;
        if (await vaultFileStore.ExistsAsync(response.UserId, ct))
        {
            var fileBytes = await vaultFileStore.ReadAsync(response.UserId, ct);
            document = VaultSerializer.Deserialize(fileBytes, _vaultKey);
        }
        else
        {
            document = VaultDocument.CreateEmpty();
            await PersistAsync(response.UserId, document, ct);
        }

        CurrentUserId = response.UserId;
        CurrentEmail = email;
        Vault = new VaultRepository(document, clock);

        try
        {
            await SyncAsync(ct);
        }
        catch (Exception)
        {
            // Best-effort initial sync: the vault stays usable offline with whatever was already on disk,
            // and the user can retry synchronisation manually.
        }
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (Vault is null)
        {
            throw new InvalidOperationException("Aucune session active.");
        }

        await syncEngine.SyncAsync(Vault, ct);
        await SaveVaultAsync(ct);
    }

    public async Task SaveVaultAsync(CancellationToken ct = default)
    {
        if (CurrentUserId is null || Vault is null)
        {
            throw new InvalidOperationException("Aucune session active.");
        }

        await PersistAsync(CurrentUserId.Value, Vault.Document, ct);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (CurrentUserId is not null)
        {
            var refreshToken = await secureStorage.GetAsync(SessionStorageKeys.RefreshToken(CurrentUserId.Value));
            if (!string.IsNullOrEmpty(refreshToken))
            {
                try
                {
                    await apiClient.LogoutAsync(refreshToken, ct);
                }
                catch (AuthApiException)
                {
                    // best-effort server-side revocation; local session is cleared regardless
                }
            }

            secureStorage.Remove(SessionStorageKeys.AccessToken(CurrentUserId.Value));
            secureStorage.Remove(SessionStorageKeys.RefreshToken(CurrentUserId.Value));
        }

        secureStorage.Remove(SessionStorageKeys.CurrentUserId);
        secureStorage.Remove(SessionStorageKeys.CurrentEmail);

        CurrentUserId = null;
        CurrentEmail = null;
        Vault = null;
        _vaultKey = null;
        _vaultSalt = null;
        _argon2Params = null;
    }

    private async Task PersistAsync(Guid userId, VaultDocument document, CancellationToken ct)
    {
        if (_vaultKey is null || _vaultSalt is null || _argon2Params is null)
        {
            throw new InvalidOperationException("Aucune clé de coffre dérivée pour cette session.");
        }

        var fileBytes = VaultSerializer.Serialize(document, _vaultKey, _vaultSalt, _argon2Params);
        await vaultFileStore.WriteAsync(userId, fileBytes, ct);
    }
}
