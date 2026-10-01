using System.Security.Cryptography;
using PassManager.Core.Crypto;
using Xunit;

namespace PassManager.Core.Tests.Crypto;

public class VaultCryptoServiceTests
{
    [Fact]
    public void DeriveKey_IsDeterministic_ForSamePasswordSaltAndParams()
    {
        var service = new VaultCryptoService();
        var salt = RandomNumberGenerator.GetBytes(16);

        var key1 = service.DeriveKey("correct horse battery staple", salt, Argon2Params.Default);
        var key2 = service.DeriveKey("correct horse battery staple", salt, Argon2Params.Default);

        Assert.Equal(key1, key2);
        Assert.Equal(VaultCryptoService.KeySizeBytes, key1.Length);
    }

    [Fact]
    public void DeriveKey_DiffersForDifferentPasswords()
    {
        var service = new VaultCryptoService();
        var salt = RandomNumberGenerator.GetBytes(16);

        var key1 = service.DeriveKey("password-one", salt, Argon2Params.Default);
        var key2 = service.DeriveKey("password-two", salt, Argon2Params.Default);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void DeriveKey_DiffersForDifferentSalts()
    {
        var service = new VaultCryptoService();

        var key1 = service.DeriveKey("same-password", RandomNumberGenerator.GetBytes(16), Argon2Params.Default);
        var key2 = service.DeriveKey("same-password", RandomNumberGenerator.GetBytes(16), Argon2Params.Default);

        Assert.NotEqual(key1, key2);
    }
}
