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
    public bool IsOffline { get; private set; }

    public Task<string?> GetLastLoginEmailAsync() => secureStorage.GetAsync(SessionStorageKeys.LastLoginEmail);

    public Task RegisterAsync(string email, string password, CancellationToken ct = default) =>
        apiClient.RegisterAsync(new RegisterRequest(email, password), ct);

    public Task ConfirmEmailAsync(string email, string token, CancellationToken ct = default) =>
        apiClient.ConfirmEmailAsync(new ConfirmEmailRequest(email, token), ct);

    public Task ResendConfirmationAsync(string email, CancellationToken ct = default) =>
        apiClient.ResendConfirmationAsync(new ResendConfirmationRequest(email), ct);

    public async Task LoginAsync(string email, string password, string deviceId, CancellationToken ct = default)
    {
        LoginResponse response;
        try
        {
            response = await apiClient.LoginAsync(new LoginRequest(email, password, deviceId), ct);
        }
        catch (Exception ex) when (ex is not AuthApiException)
        {
            // Server unreachable (DNS/connect/timeout, as opposed to a reachable server rejecting the
            // request): fall back to unlocking the local vault with credentials cached from the last
            // successful online login, instead of failing outright.
            await LoginOfflineAsync(email, password, ct);
            return;
        }

        await secureStorage.SetAsync(SessionStorageKeys.AccessToken(response.UserId), response.AccessToken);
        await secureStorage.SetAsync(SessionStorageKeys.RefreshToken(response.UserId), response.RefreshToken);
        await secureStorage.SetAsync(SessionStorageKeys.CurrentUserId, response.UserId.ToString());
        await secureStorage.SetAsync(SessionStorageKeys.CurrentEmail, email);
        await secureStorage.SetAsync(SessionStorageKeys.LastLoginEmail, email);
        await secureStorage.SetAsync(SessionStorageKeys.CachedUserId, response.UserId.ToString());
        await secureStorage.SetAsync(SessionStorageKeys.CachedVaultSalt, Convert.ToBase64String(response.VaultSalt));
        await secureStorage.SetAsync(SessionStorageKeys.CachedArgon2Params,
            $"{response.Argon2Params.MemoryKiB},{response.Argon2Params.Iterations},{response.Argon2Params.Parallelism}");

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
        IsOffline = false;

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

    private async Task LoginOfflineAsync(string email, string password, CancellationToken ct)
    {
        var cachedEmail = await secureStorage.GetAsync(SessionStorageKeys.LastLoginEmail);
        var cachedUserIdRaw = await secureStorage.GetAsync(SessionStorageKeys.CachedUserId);
        var cachedSaltRaw = await secureStorage.GetAsync(SessionStorageKeys.CachedVaultSalt);
        var cachedArgonRaw = await secureStorage.GetAsync(SessionStorageKeys.CachedArgon2Params);

        if (!string.Equals(cachedEmail, email, StringComparison.OrdinalIgnoreCase)
            || cachedUserIdRaw is null || cachedSaltRaw is null || cachedArgonRaw is null
            || !Guid.TryParse(cachedUserIdRaw, out var userId)
            || !await vaultFileStore.ExistsAsync(userId, ct))
        {
            throw new InvalidOperationException(
                "Serveur injoignable et aucune session locale disponible pour ce compte sur cet appareil.");
        }

        var argonParts = cachedArgonRaw.Split(',');
        var argon2Params = new Argon2Params(int.Parse(argonParts[0]), int.Parse(argonParts[1]), int.Parse(argonParts[2]));
        var vaultSalt = Convert.FromBase64String(cachedSaltRaw);
        var vaultKey = vaultCrypto.DeriveKey(password, vaultSalt, argon2Params);

        var fileBytes = await vaultFileStore.ReadAsync(userId, ct);
        var document = VaultSerializer.Deserialize(fileBytes, vaultKey); // throws VaultAuthenticationException on wrong password

        _vaultSalt = vaultSalt;
        _argon2Params = argon2Params;
        _vaultKey = vaultKey;
        CurrentUserId = userId;
        CurrentEmail = email;
        Vault = new VaultRepository(document, clock);
        IsOffline = true;
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (Vault is null)
        {
            throw new InvalidOperationException("Aucune session active.");
        }

        try
        {
            await syncEngine.SyncAsync(Vault, ct);
        }
        catch
        {
            IsOffline = true;
            throw;
        }

        IsOffline = false; // a successful round-trip to the server proves connectivity is back
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
        IsOffline = false;
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
