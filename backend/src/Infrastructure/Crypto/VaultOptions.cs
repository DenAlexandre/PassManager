namespace PassManager.Infrastructure.Crypto;

public class VaultOptions
{
    /// <summary>Base64-encoded 32-byte AES-256 master key used for envelope encryption of entry password/memo columns.</summary>
    public string MasterKey { get; set; } = null!;
}
