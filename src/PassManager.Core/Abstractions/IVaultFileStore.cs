namespace PassManager.Core.Abstractions;

/// <summary>Platform-specific storage of the raw encrypted .pmvault file bytes, keyed by account id.</summary>
public interface IVaultFileStore
{
    Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default);
    Task<byte[]> ReadAsync(Guid userId, CancellationToken ct = default);
    Task WriteAsync(Guid userId, byte[] fileBytes, CancellationToken ct = default);
}
