using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace PassManager.Infrastructure.Crypto;

/// <summary>
/// AES-256-GCM encryption at rest for sensitive columns (entries.password, entries.memo).
/// The server holds the master key (by design: sharing is a server-side copy, not zero-knowledge).
/// Stored format: base64(nonce[12] || tag[16] || ciphertext).
/// </summary>
public class EnvelopeEncryptionService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _masterKey;

    public EnvelopeEncryptionService(IOptions<VaultOptions> options)
    {
        _masterKey = Convert.FromBase64String(options.Value.MasterKey);
        if (_masterKey.Length != 32)
        {
            throw new InvalidOperationException("Vault:MasterKey must decode to exactly 32 bytes (AES-256).");
        }
    }

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_masterKey, TagSize))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string stored)
    {
        var raw = Convert.FromBase64String(stored);
        var nonce = raw.AsSpan(0, NonceSize);
        var tag = raw.AsSpan(NonceSize, TagSize);
        var ciphertext = raw.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        using (var aes = new AesGcm(_masterKey, TagSize))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}
