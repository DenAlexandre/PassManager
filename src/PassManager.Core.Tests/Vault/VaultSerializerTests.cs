using System.Security.Cryptography;
using PassManager.Core.Crypto;
using PassManager.Core.Models;
using PassManager.Core.Vault;
using Xunit;

namespace PassManager.Core.Tests.Vault;

public class VaultSerializerTests
{
    private static readonly Argon2Params FastParams = new(MemoryKiB: 8192, Iterations: 1, Parallelism: 1);

    private static VaultDocument BuildSampleDocument()
    {
        var document = VaultDocument.CreateEmpty();
        var root = new VaultFolder { Id = Guid.NewGuid(), ParentId = null, Name = VaultFolder.RootName, IsRoot = true, Version = 1, UpdatedAt = DateTimeOffset.UtcNow };
        document.Folders.Add(root);
        document.Entries.Add(new VaultEntry
        {
            Id = Guid.NewGuid(),
            FolderId = root.Id,
            Title = "GitHub",
            Url = "https://github.com",
            Login = "me",
            Password = "s3cr3t!",
            Memo = "note",
            Version = 1,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return document;
    }

    [Fact]
    public void SerializeThenDeserialize_RoundTripsDocumentContent()
    {
        var crypto = new VaultCryptoService();
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = crypto.DeriveKey("my-password", salt, FastParams);
        var document = BuildSampleDocument();

        var fileBytes = VaultSerializer.Serialize(document, key, salt, FastParams);
        var roundTripped = VaultSerializer.Deserialize(fileBytes, key);

        var originalEntry = document.Entries[0];
        var roundTrippedEntry = Assert.Single(roundTripped.Entries);
        Assert.Equal(originalEntry.Id, roundTrippedEntry.Id);
        Assert.Equal(originalEntry.Password, roundTrippedEntry.Password);
        Assert.Equal(originalEntry.Memo, roundTrippedEntry.Memo);
        Assert.Single(roundTripped.Folders);
        Assert.True(roundTripped.Folders[0].IsRoot);
    }

    [Fact]
    public void Deserialize_WithWrongPassword_ThrowsVaultAuthenticationException()
    {
        var crypto = new VaultCryptoService();
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = crypto.DeriveKey("right-password", salt, FastParams);
        var fileBytes = VaultSerializer.Serialize(BuildSampleDocument(), key, salt, FastParams);

        var wrongKey = crypto.DeriveKey("wrong-password", salt, FastParams);

        Assert.Throws<VaultAuthenticationException>(() => VaultSerializer.Deserialize(fileBytes, wrongKey));
    }

    [Fact]
    public void Deserialize_WithTamperedCiphertext_ThrowsVaultAuthenticationException()
    {
        var crypto = new VaultCryptoService();
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = crypto.DeriveKey("my-password", salt, FastParams);
        var fileBytes = VaultSerializer.Serialize(BuildSampleDocument(), key, salt, FastParams);

        fileBytes[^1] ^= 0xFF; // flip the last ciphertext byte

        Assert.Throws<VaultAuthenticationException>(() => VaultSerializer.Deserialize(fileBytes, key));
    }

    [Fact]
    public void Deserialize_WithTamperedHeader_ThrowsVaultAuthenticationException()
    {
        var crypto = new VaultCryptoService();
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = crypto.DeriveKey("my-password", salt, FastParams);
        var fileBytes = VaultSerializer.Serialize(BuildSampleDocument(), key, salt, FastParams);

        fileBytes[8] ^= 0xFF; // flip a byte inside the Argon2 MemoryKiB field (part of the AAD)

        Assert.Throws<VaultAuthenticationException>(() => VaultSerializer.Deserialize(fileBytes, key));
    }

    [Fact]
    public void Deserialize_WithCorruptMagic_ThrowsVaultFormatException()
    {
        var crypto = new VaultCryptoService();
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = crypto.DeriveKey("my-password", salt, FastParams);
        var fileBytes = VaultSerializer.Serialize(BuildSampleDocument(), key, salt, FastParams);

        fileBytes[0] = (byte)'X';

        Assert.Throws<VaultFormatException>(() => VaultSerializer.Deserialize(fileBytes, key));
    }

    [Fact]
    public void ReadMetadata_ReturnsSaltAndParams_WithoutRequiringTheKey()
    {
        var crypto = new VaultCryptoService();
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = crypto.DeriveKey("my-password", salt, FastParams);
        var fileBytes = VaultSerializer.Serialize(BuildSampleDocument(), key, salt, FastParams);

        var metadata = VaultSerializer.ReadMetadata(fileBytes);

        Assert.Equal(salt, metadata.Salt);
        Assert.Equal(FastParams, metadata.Argon2Params);
    }
}
