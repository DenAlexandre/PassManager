using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace PassManager.Core.KeePass;

public static class KdbxCrypto
{
    /// <summary>
    /// KDF/cipher UUIDs as raw bytes in the exact order KeePass stores them on disk — confirmed against
    /// the real DatabaseTest.kdbx fixture for AesKdfUuid (read directly from its $UUID field). System.Guid
    /// must NOT be used for these comparisons: new Guid(bytes)/Guid.ToByteArray() reorder the first three
    /// groups (.NET's mixed-endian convention), which does not match these raw on-disk bytes.
    /// </summary>
    private static readonly byte[] AesKdfUuid =
        [0xC9, 0xD9, 0xF3, 0x9A, 0x62, 0x8A, 0x44, 0x60, 0xBF, 0x74, 0x0D, 0x08, 0xC1, 0x8A, 0x4F, 0xEA];

    private static readonly byte[] Argon2dUuid =
        [0xEF, 0x63, 0x6D, 0xDF, 0x8C, 0x29, 0x44, 0x4B, 0x91, 0xF7, 0xA9, 0xA4, 0x03, 0xE3, 0x0A, 0x0C];

    private static readonly byte[] Argon2idUuidBytes =
        [0x9E, 0x29, 0x8B, 0x19, 0x56, 0xDB, 0x47, 0x73, 0xB2, 0x3D, 0xFC, 0x3E, 0xC6, 0xF0, 0xA1, 0xE6];

    public static byte[] Argon2idUuid => Argon2idUuidBytes;

    public static byte[] DeriveCompositeKey(string password)
    {
        var passwordHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password));
        return SHA256.HashData(passwordHash);
    }

    public static byte[] TransformKey(byte[] compositeKey, Dictionary<string, object> kdfParameters)
    {
        var kdfUuid = (byte[])kdfParameters["$UUID"];

        if (kdfUuid.AsSpan().SequenceEqual(AesKdfUuid))
        {
            var seed = (byte[])kdfParameters["S"];
            var rounds = (ulong)kdfParameters["R"];
            return TransformKeyAesKdf(compositeKey, seed, rounds);
        }

        if (kdfUuid.AsSpan().SequenceEqual(Argon2dUuid))
        {
            return TransformKeyArgon2(compositeKey, kdfParameters, isArgon2id: false);
        }

        if (kdfUuid.AsSpan().SequenceEqual(Argon2idUuidBytes))
        {
            return TransformKeyArgon2(compositeKey, kdfParameters, isArgon2id: true);
        }

        throw new KdbxFormatException($"KDF non supporté : {Convert.ToHexString(kdfUuid)}.");
    }

    private static byte[] TransformKeyAesKdf(byte[] compositeKey, byte[] seed, ulong rounds)
    {
        using var aes = Aes.Create();
        aes.Key = seed;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using var encryptor = aes.CreateEncryptor();

        var left = compositeKey[..16];
        var right = compositeKey[16..];

        for (ulong i = 0; i < rounds; i++)
        {
            encryptor.TransformBlock(left, 0, 16, left, 0);
            encryptor.TransformBlock(right, 0, 16, right, 0);
        }

        var combined = new byte[32];
        Array.Copy(left, 0, combined, 0, 16);
        Array.Copy(right, 0, combined, 16, 16);
        return SHA256.HashData(combined);
    }

    private static byte[] TransformKeyArgon2(byte[] compositeKey, Dictionary<string, object> kdfParameters, bool isArgon2id)
    {
        var salt = (byte[])kdfParameters["S"];
        var iterations = (int)(ulong)kdfParameters["I"];
        var memoryKiB = (int)((ulong)kdfParameters["M"] / 1024);
        var parallelism = (int)(uint)kdfParameters["P"];

        Argon2 argon2 = isArgon2id
            ? new Argon2id(compositeKey)
            : new Argon2d(compositeKey);
        argon2.Salt = salt;
        argon2.DegreeOfParallelism = parallelism;
        argon2.Iterations = iterations;
        argon2.MemorySize = memoryKiB;

        return argon2.GetBytes(32);
    }

    public static byte[] DeriveFinalKey(byte[] masterSeed, byte[] transformedKey) =>
        SHA256.HashData([.. masterSeed, .. transformedKey]);

    public static byte[] DeriveHmacKeyBase(byte[] masterSeed, byte[] transformedKey) =>
        SHA512.HashData([.. masterSeed, .. transformedKey, 0x01]);

    private static byte[] DeriveBlockHmacKey(byte[] hmacKeyBase, ulong blockIndex) =>
        SHA512.HashData([.. BitConverter.GetBytes(blockIndex), .. hmacKeyBase]);

    public static void VerifyHeaderHmac(byte[] headerBytes, byte[] expectedHmac, byte[] hmacKeyBase)
    {
        var blockKey = DeriveBlockHmacKey(hmacKeyBase, ulong.MaxValue);
        using var hmac = new HMACSHA256(blockKey);
        var actual = hmac.ComputeHash(headerBytes);
        if (!actual.AsSpan().SequenceEqual(expectedHmac))
        {
            throw new KdbxFormatException("Mot de passe incorrect ou fichier invalide (HMAC d'en-tête).");
        }
    }

    public static byte[] ReadHmacBlockStream(Stream input, byte[] hmacKeyBase)
    {
        using var output = new MemoryStream();
        ulong blockIndex = 0;

        while (true)
        {
            var storedHmac = new byte[32];
            ReadExact(input, storedHmac, 32);

            var lengthBytes = new byte[4];
            ReadExact(input, lengthBytes, 4);
            var length = BitConverter.ToInt32(lengthBytes, 0);

            var data = new byte[length];
            if (length > 0)
            {
                ReadExact(input, data, length);
            }

            var blockKey = DeriveBlockHmacKey(hmacKeyBase, blockIndex);
            using var hmac = new HMACSHA256(blockKey);
            hmac.TransformBlock(BitConverter.GetBytes(blockIndex), 0, 8, null, 0);
            hmac.TransformBlock(lengthBytes, 0, 4, null, 0);
            if (length > 0)
            {
                hmac.TransformBlock(data, 0, length, null, 0);
            }

            hmac.TransformFinalBlock([], 0, 0);
            if (!hmac.Hash!.AsSpan().SequenceEqual(storedHmac))
            {
                throw new KdbxFormatException("Mot de passe incorrect ou fichier invalide (HMAC de bloc).");
            }

            if (length == 0)
            {
                break;
            }

            output.Write(data, 0, length);
            blockIndex += 1;
        }

        return output.ToArray();
    }

    public static void WriteHmacBlockStream(Stream output, byte[] plaintext, byte[] hmacKeyBase)
    {
        const int blockSize = 1024 * 1024;
        ulong blockIndex = 0;
        var position = 0;

        while (position < plaintext.Length)
        {
            var length = Math.Min(blockSize, plaintext.Length - position);
            var data = plaintext[position..(position + length)];
            WriteBlock(output, data, blockIndex, hmacKeyBase);
            position += length;
            blockIndex += 1;
        }

        WriteBlock(output, [], blockIndex, hmacKeyBase);
    }

    private static void WriteBlock(Stream output, byte[] data, ulong blockIndex, byte[] hmacKeyBase)
    {
        var lengthBytes = BitConverter.GetBytes(data.Length);
        var blockKey = DeriveBlockHmacKey(hmacKeyBase, blockIndex);

        using var hmac = new HMACSHA256(blockKey);
        hmac.TransformBlock(BitConverter.GetBytes(blockIndex), 0, 8, null, 0);
        hmac.TransformBlock(lengthBytes, 0, 4, null, 0);
        if (data.Length > 0)
        {
            hmac.TransformBlock(data, 0, data.Length, null, 0);
        }

        hmac.TransformFinalBlock([], 0, 0);

        output.Write(hmac.Hash!, 0, 32);
        output.Write(lengthBytes, 0, 4);
        output.Write(data, 0, data.Length);
    }

    public static byte[] ComputeHeaderHmac(byte[] headerBytes, byte[] hmacKeyBase)
    {
        var blockKey = DeriveBlockHmacKey(hmacKeyBase, ulong.MaxValue);
        using var hmac = new HMACSHA256(blockKey);
        return hmac.ComputeHash(headerBytes);
    }

    private static void ReadExact(Stream stream, byte[] buffer, int count)
    {
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n == 0)
            {
                throw new KdbxFormatException("Fichier KDBX tronqué ou corrompu.");
            }

            read += n;
        }
    }
}
