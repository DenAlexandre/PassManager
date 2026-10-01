using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PassManager.Core.Crypto;

namespace PassManager.Core.Vault;

public record VaultFileMetadata(ushort FormatVersion, Argon2Params Argon2Params, byte[] Salt);

/// <summary>
/// Reads/writes the .pmvault binary container: [AAD header][tag][AES-256-GCM ciphertext of the VaultDocument JSON].
/// </summary>
public static class VaultSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static byte[] Serialize(VaultDocument document, byte[] key, byte[] kdfSalt, Argon2Params argon2Params)
    {
        var nonce = RandomNumberGenerator.GetBytes(VaultFileHeader.NonceSize);
        var header = new VaultFileHeader
        {
            FormatVersion = VaultFileHeader.CurrentFormatVersion,
            Argon2Params = argon2Params,
            Salt = kdfSalt,
            Nonce = nonce
        };
        var aad = header.ToAadBytes();

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[VaultFileHeader.TagSize];

        using (var aes = new AesGcm(key, VaultFileHeader.TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        }

        var result = new byte[aad.Length + tag.Length + ciphertext.Length];
        var span = result.AsSpan();
        aad.CopyTo(span);
        tag.CopyTo(span[aad.Length..]);
        ciphertext.CopyTo(span[(aad.Length + tag.Length)..]);

        return result;
    }

    public static VaultDocument Deserialize(byte[] fileBytes, byte[] key)
    {
        if (fileBytes.Length < VaultFileHeader.AadSize + VaultFileHeader.TagSize)
        {
            throw new VaultFormatException("Fichier de coffre tronqué.");
        }

        var aad = fileBytes.AsSpan(0, VaultFileHeader.AadSize);
        var header = VaultFileHeader.Parse(aad);
        var tag = fileBytes.AsSpan(VaultFileHeader.AadSize, VaultFileHeader.TagSize);
        var ciphertext = fileBytes.AsSpan(VaultFileHeader.AadSize + VaultFileHeader.TagSize);

        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, VaultFileHeader.TagSize);
            aes.Decrypt(header.Nonce, ciphertext, tag, plaintext, aad.ToArray());
        }
        catch (CryptographicException ex)
        {
            throw new VaultAuthenticationException("Mot de passe incorrect ou coffre corrompu.", ex);
        }

        try
        {
            return JsonSerializer.Deserialize<VaultDocument>(plaintext, JsonOptions)
                ?? throw new VaultFormatException("Contenu du coffre illisible.");
        }
        catch (JsonException)
        {
            throw new VaultFormatException("Contenu du coffre illisible.");
        }
    }

    /// <summary>Reads the KDF salt/params embedded in the file header without needing the derived key.</summary>
    public static VaultFileMetadata ReadMetadata(byte[] fileBytes)
    {
        if (fileBytes.Length < VaultFileHeader.AadSize + VaultFileHeader.TagSize)
        {
            throw new VaultFormatException("Fichier de coffre tronqué.");
        }

        var header = VaultFileHeader.Parse(fileBytes.AsSpan(0, VaultFileHeader.AadSize));
        return new VaultFileMetadata(header.FormatVersion, header.Argon2Params, header.Salt);
    }
}
