namespace PassManager.Core.Abstractions;

/// <summary>Platform-specific secure key/value storage (JWTs, cached KDF salt/params) — e.g. MAUI SecureStorage.</summary>
public interface ISecureStorageService
{
    Task SetAsync(string key, string value);
    Task<string?> GetAsync(string key);
    void Remove(string key);
}
