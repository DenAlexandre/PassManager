using PassManager.Core.Abstractions;

namespace PassManager.Maui.Services.Platform;

public class VaultFileStore : IVaultFileStore
{
    public Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(GetPath(userId)));

    public async Task<byte[]> ReadAsync(Guid userId, CancellationToken ct = default) =>
        await File.ReadAllBytesAsync(GetPath(userId), ct);

    public async Task WriteAsync(Guid userId, byte[] fileBytes, CancellationToken ct = default)
    {
        var path = GetPath(userId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, fileBytes, ct);
    }

    private static string GetPath(Guid userId) =>
        Path.Combine(FileSystem.AppDataDirectory, "vaults", $"{userId}.pmvault");
}
