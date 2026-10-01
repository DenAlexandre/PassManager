using System.Text;
using Konscious.Security.Cryptography;

namespace PassManager.Core.Crypto;

/// <summary>Derives the local vault encryption key from the account password via Argon2id.</summary>
public class VaultCryptoService
{
    public const int KeySizeBytes = 32;

    public byte[] DeriveKey(string password, byte[] salt, Argon2Params argon2Params)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = argon2Params.Parallelism,
                Iterations = argon2Params.Iterations,
                MemorySize = argon2Params.MemoryKiB
            };

            return argon2.GetBytes(KeySizeBytes);
        }
        finally
        {
            Array.Clear(passwordBytes);
        }
    }
}
