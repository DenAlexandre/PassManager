using System.Buffers.Binary;
using System.Text;
using PassManager.Core.Crypto;

namespace PassManager.Core.Vault;

/// <summary>
/// Fixed binary header for the .pmvault container: magic, format/KDF/cipher ids, Argon2 params, KDF salt and AES-GCM nonce.
/// Everything here is stored in clear (it must be readable before the vault key can be derived) and is authenticated
/// as AES-GCM associated data, so tampering with any of these fields is detected on decrypt.
/// </summary>
public sealed class VaultFileHeader
{
    public const string MagicValue = "PMVL";
    public const ushort CurrentFormatVersion = 1;
    public const byte KdfIdArgon2id = 1;
    public const byte CipherIdAes256Gcm = 1;

    public const int SaltSize = 16;
    public const int NonceSize = 12;
    public const int TagSize = 16;

    // magic(4) + version(2) + kdfId(1) + cipherId(1) + memoryKiB(4) + iterations(4) + parallelism(1) + salt(16) + nonce(12)
    public const int AadSize = 4 + 2 + 1 + 1 + 4 + 4 + 1 + SaltSize + NonceSize;

    public required ushort FormatVersion { get; init; }
    public required Argon2Params Argon2Params { get; init; }
    public required byte[] Salt { get; init; }
    public required byte[] Nonce { get; init; }

    public byte[] ToAadBytes()
    {
        var buffer = new byte[AadSize];
        var span = buffer.AsSpan();

        Encoding.ASCII.GetBytes(MagicValue, span[..4]);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(4, 2), FormatVersion);
        span[6] = KdfIdArgon2id;
        span[7] = CipherIdAes256Gcm;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8, 4), Argon2Params.MemoryKiB);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12, 4), Argon2Params.Iterations);
        span[16] = (byte)Argon2Params.Parallelism;
        Salt.CopyTo(span.Slice(17, SaltSize));
        Nonce.CopyTo(span.Slice(17 + SaltSize, NonceSize));

        return buffer;
    }

    public static VaultFileHeader Parse(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length < AadSize)
        {
            throw new VaultFormatException("En-tête du coffre tronqué.");
        }

        var magic = Encoding.ASCII.GetString(headerBytes[..4]);
        if (magic != MagicValue)
        {
            throw new VaultFormatException("Ce fichier n'est pas un coffre PassManager valide.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.Slice(4, 2));
        if (version != CurrentFormatVersion)
        {
            throw new VaultFormatException($"Version de format de coffre non supportée : {version}.");
        }

        var kdfId = headerBytes[6];
        var cipherId = headerBytes[7];
        if (kdfId != KdfIdArgon2id || cipherId != CipherIdAes256Gcm)
        {
            throw new VaultFormatException("Algorithmes de chiffrement du coffre non reconnus.");
        }

        var memoryKiB = BinaryPrimitives.ReadInt32LittleEndian(headerBytes.Slice(8, 4));
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(headerBytes.Slice(12, 4));
        var parallelism = headerBytes[16];
        var salt = headerBytes.Slice(17, SaltSize).ToArray();
        var nonce = headerBytes.Slice(17 + SaltSize, NonceSize).ToArray();

        return new VaultFileHeader
        {
            FormatVersion = version,
            Argon2Params = new Argon2Params(memoryKiB, iterations, parallelism),
            Salt = salt,
            Nonce = nonce
        };
    }
}
