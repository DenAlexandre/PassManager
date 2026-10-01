# KeePass (.kdbx) Import/Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (recommended for this plan) or superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **No automated tests in this plan (explicit user decision) — every task is verified manually as described in each task's verification step.**

**Goal:** Let a user import an existing KeePass `.kdbx` (KDBX4) database into their PassManager vault, and export their vault as a real `.kdbx` file KeePass/KeePassXC can open.

**Architecture:** A from-scratch KDBX4 reader/writer in `PassManager.Core` (no MAUI reference, no external KeePass library — reuses the already-referenced Argon2 package and .NET's built-in AES/HMAC/GZip), mapped to/from the existing `VaultRepository` via two small services, wired into the vault screen through a new "⋯" menu.

**Tech Stack:** C# / .NET 10, `Konscious.Security.Cryptography.Argon2` (existing dependency), `System.Security.Cryptography` (AES, HMACSHA256, SHA256, SHA512 — all built-in), `System.IO.Compression.GZipStream` (built-in), `System.Xml.Linq` (built-in), MAUI `FilePicker` (built-in Essentials) + `CommunityToolkit.Maui.Storage.FileSaver` (existing dependency).

**Spec:** `docs/superpowers/specs/2026-10-01-keepass-import-export-design.md`

## Global Constraints

- KDBX4 only — reject any other version with a clear error.
- Outer cipher: AES256-CBC only (UUID `31C1F2E6-BF71-4350-BE58-05216AFC5AFF`) — reject anything else.
- KDF: AES-KDF (UUID `C9D9F39A-628A-4460-BF74-0D08C18A4FEA`), Argon2d (UUID `EF636DDF-8C29-444B-91F7-A9A403E30A0C`), or Argon2id (UUID `9E298B19-56DB-4773-B23D-FC3EC6F0A1E6`) — reject anything else.
- Inner protected-value stream: ChaCha20 only (`InnerRandomStreamID == 3`) — reject Salsa20/ArcFour/None.
- Fields carried: Title, URL, UserName→Login, Password, Notes→Memo. Everything else (attachments, icons, custom fields, tags, expiry, TOTP, history) is silently dropped on import and never emitted on export.
- No keyfile support — password-only KDBX databases only.
- Import always creates one new top-level folder under the vault's root (never merges into existing folders).
- Export covers the entire vault only (no per-folder export).
- **No automated tests** — every task's verification step is manual, run by you (the implementer) in this session, using the real fixture `src/PassManager.Core.Tests/Fixtures/DatabaseTest.kdbx` (master password `Semeru1234@2026`).
- If a verification step fails, use `superpowers:systematic-debugging` — compare against the confirmed real byte values in this plan and the spec, don't guess.

## Review Focus

- A `.kdbx` file with a correct password but a KDF/cipher/inner-stream this implementation doesn't support must produce a clear "unsupported" state, not a crash or silent garbage import (Task 4, Task 8).
- A wrong password must produce the generic incorrect-password message, never partial/garbage entries (Task 4's HMAC check must fail closed, Task 7's UI catch).
- An entry whose `UserName`/`URL`/`Notes` field is absent in the source XML (optional fields) must import as `null`, not throw or produce the literal string `"null"` (Task 4, Task 6).
- Cancelling the file picker or file saver dialog must not show an error message — only a genuine I/O or format failure should (Task 7).
- Re-importing a `.kdbx` file this app just exported must succeed and reproduce the same entries — the only way to prove the writer and reader agree with each other, independent of whether either agrees with real KeePass (Task 8).

---

### Task 1: KdbxDocument model, KdbxFormatException, VariantDictionary

**Files:**
- Create: `src/PassManager.Core/KeePass/KdbxDocument.cs`
- Create: `src/PassManager.Core/KeePass/KdbxFormatException.cs`
- Create: `src/PassManager.Core/KeePass/VariantDictionary.cs`

**Interfaces:**
- Produces: `KdbxGroup { string Name; List<KdbxGroup> Groups; List<KdbxEntry> Entries; }`, `KdbxEntry { string Title; string? Url; string? UserName; string Password; string? Notes; }`, `KdbxDocument { KdbxGroup Root; }` — consumed by every later task.
- Produces: `KdbxFormatException` — consumed by every later task as the single exception type for format/crypto failures.
- Produces: `VariantDictionary.Read(byte[] data) -> Dictionary<string, object>` and `VariantDictionary.Write(Dictionary<string, object> values) -> byte[]` — consumed by Task 3 (KdbxCrypto) and Task 4 (KdbxReader).

- [ ] **Step 1: Create the document model**

```csharp
namespace PassManager.Core.KeePass;

public class KdbxGroup
{
    public string Name { get; set; } = "";
    public List<KdbxGroup> Groups { get; } = [];
    public List<KdbxEntry> Entries { get; } = [];
}

public class KdbxEntry
{
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public string? UserName { get; set; }
    public string Password { get; set; } = "";
    public string? Notes { get; set; }
}

public class KdbxDocument
{
    public KdbxGroup Root { get; set; } = new();
}
```

- [ ] **Step 2: Create the exception type**

```csharp
namespace PassManager.Core.KeePass;

public class KdbxFormatException : Exception
{
    public KdbxFormatException(string message) : base(message)
    {
    }
}
```

- [ ] **Step 3: Implement VariantDictionary**

```csharp
using System.Text;

namespace PassManager.Core.KeePass;

/// <summary>
/// Reads/writes the binary type-tagged key-value map used for KdbxParameters (KDF settings).
/// Format: UInt16 version, then repeated (byte type, Int32 keyLength, byte[] key, Int32 valueLength,
/// byte[] value) entries, terminated by a type-0x00 byte. Confirmed byte-for-byte against the real
/// DatabaseTest.kdbx fixture's KdfParameters blob (see the design spec).
/// </summary>
public static class VariantDictionary
{
    private const byte TypeUInt32 = 0x04;
    private const byte TypeUInt64 = 0x05;
    private const byte TypeBool = 0x08;
    private const byte TypeInt32 = 0x0C;
    private const byte TypeInt64 = 0x0D;
    private const byte TypeString = 0x18;
    private const byte TypeByteArray = 0x42;
    private const byte TypeEnd = 0x00;

    public static Dictionary<string, object> Read(byte[] data)
    {
        var result = new Dictionary<string, object>();
        var offset = 0;

        var version = BitConverter.ToUInt16(data, offset);
        offset += 2;
        if ((version >> 8) != 1)
        {
            throw new KdbxFormatException($"Version de VariantDictionary non supportée : 0x{version:X4}.");
        }

        while (true)
        {
            var type = data[offset];
            offset += 1;
            if (type == TypeEnd)
            {
                break;
            }

            var keyLength = BitConverter.ToInt32(data, offset);
            offset += 4;
            var key = Encoding.UTF8.GetString(data, offset, keyLength);
            offset += keyLength;

            var valueLength = BitConverter.ToInt32(data, offset);
            offset += 4;
            var valueBytes = new byte[valueLength];
            Array.Copy(data, offset, valueBytes, 0, valueLength);
            offset += valueLength;

            object value = type switch
            {
                TypeUInt32 => BitConverter.ToUInt32(valueBytes, 0),
                TypeUInt64 => BitConverter.ToUInt64(valueBytes, 0),
                TypeBool => valueBytes[0] != 0,
                TypeInt32 => BitConverter.ToInt32(valueBytes, 0),
                TypeInt64 => BitConverter.ToInt64(valueBytes, 0),
                TypeString => Encoding.UTF8.GetString(valueBytes),
                TypeByteArray => valueBytes,
                _ => throw new KdbxFormatException($"Type de VariantDictionary non supporté : 0x{type:X2}.")
            };

            result[key] = value;
        }

        return result;
    }

    public static byte[] Write(Dictionary<string, object> values)
    {
        using var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes((ushort)0x0100));

        foreach (var (key, value) in values)
        {
            byte type;
            byte[] valueBytes;
            switch (value)
            {
                case uint u32:
                    type = TypeUInt32;
                    valueBytes = BitConverter.GetBytes(u32);
                    break;
                case ulong u64:
                    type = TypeUInt64;
                    valueBytes = BitConverter.GetBytes(u64);
                    break;
                case byte[] bytes:
                    type = TypeByteArray;
                    valueBytes = bytes;
                    break;
                default:
                    throw new KdbxFormatException($"Type non supporté pour l'écriture : {value.GetType()}.");
            }

            stream.WriteByte(type);
            var keyBytes = Encoding.UTF8.GetBytes(key);
            stream.Write(BitConverter.GetBytes(keyBytes.Length));
            stream.Write(keyBytes);
            stream.Write(BitConverter.GetBytes(valueBytes.Length));
            stream.Write(valueBytes);
        }

        stream.WriteByte(TypeEnd);
        return stream.ToArray();
    }
}
```

- [ ] **Step 4: Build to confirm it compiles**

Run: `dotnet build src/PassManager.Core/PassManager.Core.csproj`
Expected: Build succeeds, 0 errors.

- [ ] **Step 5: Manual verification — parse the real fixture's KdfParameters blob**

Temporarily add this to the bottom of `MauiProgram.cs`'s `CreateMauiApp()` method, right before `return builder.Build();` (remove it again in Step 6):

```csharp
#if DEBUG
		var kdfParamsHex = "0001420500000024555549441000000...";
#endif
```

Actually, simpler: instead of hand-copying hex, read the real file directly. Add this temporary block instead (still right before `return builder.Build();`):

```csharp
#if DEBUG
		var fixturePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PassManager.Core.Tests", "Fixtures", "DatabaseTest.kdbx");
		if (File.Exists(fixturePath))
		{
			var fileBytes = File.ReadAllBytes(fixturePath);
			// KdfParameters field starts at offset 0x54 (field header) per the spec's hand-decoded bytes;
			// its data (93 bytes, offset 0x54+9=0x5D... ) — simplest: locate field id 11 (0x0B) by scanning.
			var offset = 12; // after signature(8) + version(4)
			while (fileBytes[offset] != 11)
			{
				offset += 1;
				var len = BitConverter.ToInt32(fileBytes, offset);
				offset += 4 + len;
			}
			offset += 1;
			var kdfLen = BitConverter.ToInt32(fileBytes, offset);
			offset += 4;
			var kdfBytes = fileBytes[offset..(offset + kdfLen)];
			var parsed = PassManager.Core.KeePass.VariantDictionary.Read(kdfBytes);
			var uuidBytes = (byte[])parsed["$UUID"];
			System.Diagnostics.Debug.WriteLine($"KDF UUID = {new Guid(uuidBytes)}");
			System.Diagnostics.Debug.WriteLine($"Rounds R = {parsed["R"]}");
			System.Diagnostics.Debug.WriteLine($"Seed S length = {((byte[])parsed["S"]).Length}");
		}
#endif
```

Run: `dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0` then launch the exe and check the Debug Output window (or redirect `System.Diagnostics.Debug.WriteLine` — if not visible, temporarily swap to `Console.WriteLine` and run from a terminal instead).
Expected output: `KDF UUID = c9d9f39a-628a-4460-bf74-0d08c18a4fea`, `Rounds R = 600000`, `Seed S length = 32`. These exact values were hand-confirmed against the fixture's real bytes during design — if they don't match, the bug is in this task's `VariantDictionary.Read`, not in later tasks.

- [ ] **Step 6: Remove the temporary verification block from MauiProgram.cs**

Delete the `#if DEBUG ... #endif` block added in Step 5. Rebuild to confirm it still compiles clean.

- [ ] **Step 7: Commit**

```bash
git add src/PassManager.Core/KeePass/KdbxDocument.cs src/PassManager.Core/KeePass/KdbxFormatException.cs src/PassManager.Core/KeePass/VariantDictionary.cs
git commit -m "feat: add KDBX document model and VariantDictionary parser"
```

---

### Task 2: ChaCha20Cipher

**Files:**
- Create: `src/PassManager.Core/KeePass/ChaCha20Cipher.cs`

**Interfaces:**
- Produces: `ChaCha20Cipher.Apply(byte[] key, byte[] nonce, byte[] data) -> byte[]` (symmetric — same call encrypts or decrypts, since it's a pure keystream XOR) — consumed by Task 4 (KdbxReader) and Task 5 (KdbxWriter) for inner protected-value fields.

This is the raw IETF ChaCha20 (RFC 7539: 256-bit key, 96-bit/12-byte nonce, 32-bit block counter starting at 0, 20 rounds), **not** `System.Security.Cryptography.ChaCha20Poly1305` (that class is AEAD-only — it requires and produces an authentication tag and cannot be used as a bare keystream generator).

- [ ] **Step 1: Implement the cipher**

```csharp
namespace PassManager.Core.KeePass;

/// <summary>Raw IETF ChaCha20 (RFC 7539) keystream XOR — not AEAD. Used only for KDBX inner protected-value decryption.</summary>
public static class ChaCha20Cipher
{
    public static byte[] Apply(byte[] key, byte[] nonce, byte[] data)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("La clé ChaCha20 doit faire 32 octets.", nameof(key));
        }

        if (nonce.Length != 12)
        {
            throw new ArgumentException("Le nonce ChaCha20 doit faire 12 octets.", nameof(nonce));
        }

        var result = new byte[data.Length];
        var block = new uint[16];
        uint counter = 0;

        for (var offset = 0; offset < data.Length; offset += 64)
        {
            InitBlock(block, key, nonce, counter);
            var keystream = RunBlock(block);

            var chunkSize = Math.Min(64, data.Length - offset);
            for (var i = 0; i < chunkSize; i++)
            {
                var keystreamByte = (byte)(keystream[i / 4] >> (8 * (i % 4)));
                result[offset + i] = (byte)(data[offset + i] ^ keystreamByte);
            }

            counter += 1;
        }

        return result;
    }

    private static void InitBlock(uint[] block, byte[] key, byte[] nonce, uint counter)
    {
        block[0] = 0x61707865;
        block[1] = 0x3320646e;
        block[2] = 0x79622d32;
        block[3] = 0x6b206574;
        for (var i = 0; i < 8; i++)
        {
            block[4 + i] = BitConverter.ToUInt32(key, i * 4);
        }

        block[12] = counter;
        block[13] = BitConverter.ToUInt32(nonce, 0);
        block[14] = BitConverter.ToUInt32(nonce, 4);
        block[15] = BitConverter.ToUInt32(nonce, 8);
    }

    private static uint[] RunBlock(uint[] initial)
    {
        var state = (uint[])initial.Clone();

        for (var round = 0; round < 10; round++)
        {
            QuarterRound(state, 0, 4, 8, 12);
            QuarterRound(state, 1, 5, 9, 13);
            QuarterRound(state, 2, 6, 10, 14);
            QuarterRound(state, 3, 7, 11, 15);
            QuarterRound(state, 0, 5, 10, 15);
            QuarterRound(state, 1, 6, 11, 12);
            QuarterRound(state, 2, 7, 8, 13);
            QuarterRound(state, 3, 4, 9, 14);
        }

        var output = new uint[16];
        for (var i = 0; i < 16; i++)
        {
            output[i] = state[i] + initial[i];
        }

        return output;
    }

    private static void QuarterRound(uint[] s, int a, int b, int c, int d)
    {
        s[a] += s[b]; s[d] ^= s[a]; s[d] = RotateLeft(s[d], 16);
        s[c] += s[d]; s[b] ^= s[c]; s[b] = RotateLeft(s[b], 12);
        s[a] += s[b]; s[d] ^= s[a]; s[d] = RotateLeft(s[d], 8);
        s[c] += s[d]; s[b] ^= s[c]; s[b] = RotateLeft(s[b], 7);
    }

    private static uint RotateLeft(uint value, int bits) => (value << bits) | (value >> (32 - bits));
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/PassManager.Core/PassManager.Core.csproj`
Expected: Build succeeds, 0 errors.

- [ ] **Step 3: Manual verification against the RFC 7539 §2.4.2 reference test vector**

Add this temporarily to the same `#if DEBUG` spot in `MauiProgram.cs` used in Task 1 (or reuse/re-add the block):

```csharp
#if DEBUG
		var testKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
		var testNonce = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4a, 0x00, 0x00, 0x00, 0x00 };
		var plaintext = System.Text.Encoding.ASCII.GetBytes(
			"Ladies and Gentlemen of the class of '99: If I could offer you only one tip for the future, sunscreen would be it.");
		// RFC 7539 test vector uses an initial counter of 1, not 0 — call Apply starting from a 1-block offset
		// by prepending 64 bytes of dummy data at counter 0, then discarding that first block:
		var padded = new byte[64 + plaintext.Length];
		Array.Copy(plaintext, 0, padded, 64, plaintext.Length);
		var encryptedPadded = PassManager.Core.KeePass.ChaCha20Cipher.Apply(testKey, testNonce, padded);
		var ciphertext = encryptedPadded[64..];
		System.Diagnostics.Debug.WriteLine($"ChaCha20 RFC vector ciphertext[0..8] hex = {Convert.ToHexString(ciphertext[..8])}");
#endif
```

Run: `dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0`, launch, check output.
Expected: `6e2e359a2568f98041ba0728dd0d6981` is the start of the RFC 7539 §2.4.2 published ciphertext for this exact key/nonce/plaintext/counter=1 — the first 8 bytes should print as `6E2E359A2568F980`. If they don't match, the bug is in `ChaCha20Cipher`, not anywhere downstream.

- [ ] **Step 4: Remove the temporary verification block**

Delete the `#if DEBUG ... #endif` block. Rebuild to confirm clean.

- [ ] **Step 5: Commit**

```bash
git add src/PassManager.Core/KeePass/ChaCha20Cipher.cs
git commit -m "feat: add raw ChaCha20 keystream cipher for KDBX inner stream"
```

---

### Task 3: KdbxCrypto (key derivation + HMAC block stream read)

**Files:**
- Create: `src/PassManager.Core/KeePass/KdbxCrypto.cs`

**Interfaces:**
- Consumes: `VariantDictionary.Read` (Task 1), `Konscious.Security.Cryptography.Argon2d`/`Argon2id` (existing package).
- Produces:
  - `KdbxCrypto.DeriveCompositeKey(string password) -> byte[]` (32 bytes)
  - `KdbxCrypto.TransformKey(byte[] compositeKey, Dictionary<string, object> kdfParameters) -> byte[]` (32 bytes) — dispatches to AES-KDF or Argon2 based on `$UUID`.
  - `KdbxCrypto.DeriveFinalKey(byte[] masterSeed, byte[] transformedKey) -> byte[]` (32 bytes, AES key)
  - `KdbxCrypto.DeriveHmacKeyBase(byte[] masterSeed, byte[] transformedKey) -> byte[]` (64 bytes)
  - `KdbxCrypto.ReadHmacBlockStream(Stream input, byte[] hmacKeyBase) -> byte[]` (concatenated, authenticated ciphertext) — throws `KdbxFormatException` on any HMAC mismatch.
  - Consumed by Task 4 (`KdbxReader`).

- [ ] **Step 1: Implement KdbxCrypto (derivation + block-stream read half)**

```csharp
using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace PassManager.Core.KeePass;

public static class KdbxCrypto
{
    private static readonly Guid AesKdfUuid = Guid.Parse("c9d9f39a-628a-4460-bf74-0d08c18a4fea");
    private static readonly Guid Argon2dUuid = Guid.Parse("ef636ddf-8c29-444b-91f7-a9a403e30a0c");
    private static readonly Guid Argon2idUuid = Guid.Parse("9e298b19-56db-4773-b23d-fc3ec6f0a1e6");

    public static byte[] DeriveCompositeKey(string password)
    {
        var passwordHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password));
        return SHA256.HashData(passwordHash);
    }

    public static byte[] TransformKey(byte[] compositeKey, Dictionary<string, object> kdfParameters)
    {
        var kdfUuid = new Guid((byte[])kdfParameters["$UUID"]);

        if (kdfUuid == AesKdfUuid)
        {
            var seed = (byte[])kdfParameters["S"];
            var rounds = (ulong)kdfParameters["R"];
            return TransformKeyAesKdf(compositeKey, seed, rounds);
        }

        if (kdfUuid == Argon2dUuid || kdfUuid == Argon2idUuid)
        {
            return TransformKeyArgon2(compositeKey, kdfParameters, isArgon2id: kdfUuid == Argon2idUuid);
        }

        throw new KdbxFormatException($"KDF non supporté : {kdfUuid}.");
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
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/PassManager.Core/PassManager.Core.csproj`
Expected: Build succeeds, 0 errors. If `Argon2d`/`Argon2id` constructors or `Argon2.Salt`/`.DegreeOfParallelism`/`.Iterations`/`.MemorySize`/`.GetBytes` don't match the actual `Konscious.Security.Cryptography.Argon2` v1.3.1 API, fix the member names to match — check `src/PassManager.Core/Crypto/VaultCryptoService.cs` (the existing Argon2id usage in this codebase) for the exact API shape already in use and mirror it.

- [ ] **Step 3: Commit**

```bash
git add src/PassManager.Core/KeePass/KdbxCrypto.cs
git commit -m "feat: add KDBX key derivation (AES-KDF + Argon2) and HMAC block-stream reader"
```

---

### Task 4: KdbxReader

**Files:**
- Create: `src/PassManager.Core/KeePass/KdbxReader.cs`

**Interfaces:**
- Consumes: `VariantDictionary.Read` (Task 1), `KdbxCrypto.*` (Task 3), `ChaCha20Cipher.Apply` (Task 2), `KdbxDocument`/`KdbxGroup`/`KdbxEntry` (Task 1).
- Produces: `KdbxReader.Read(Stream kdbxFile, string password) -> KdbxDocument` — consumed by Task 6 (`KdbxImportService`) and the MAUI import flow (Task 7).

This is the task most likely to need debugging — its manual verification step (Step 3) is the first point where every earlier primitive is exercised together against the real fixture.

- [ ] **Step 1: Implement the outer-header parser and full read pipeline**

```csharp
using System.IO.Compression;
using System.Xml.Linq;

namespace PassManager.Core.KeePass;

public static class KdbxReader
{
    private static readonly Guid Aes256CipherUuid = Guid.Parse("31c1f2e6-bf71-4350-be58-05216afc5aff");

    public static KdbxDocument Read(Stream kdbxFile, string password)
    {
        using var ms = new MemoryStream();
        kdbxFile.CopyTo(ms);
        var fileBytes = ms.ToArray();

        var sig1 = BitConverter.ToUInt32(fileBytes, 0);
        var sig2 = BitConverter.ToUInt32(fileBytes, 4);
        if (sig1 != 0x9AA2D903 || sig2 != 0xB54BFB67)
        {
            throw new KdbxFormatException("Signature KDBX invalide.");
        }

        var versionMinor = BitConverter.ToUInt16(fileBytes, 8);
        var versionMajor = BitConverter.ToUInt16(fileBytes, 10);
        if (versionMajor != 4)
        {
            throw new KdbxFormatException($"Seul KDBX4 est supporté (version trouvée : {versionMajor}.{versionMinor}).");
        }

        var offset = 12;
        byte[]? cipherId = null;
        uint compressionFlags = 0;
        byte[]? masterSeed = null;
        byte[]? encryptionIv = null;
        Dictionary<string, object>? kdfParameters = null;

        while (true)
        {
            var fieldId = fileBytes[offset];
            offset += 1;
            var fieldLength = BitConverter.ToInt32(fileBytes, offset);
            offset += 4;
            var fieldData = fileBytes[offset..(offset + fieldLength)];
            offset += fieldLength;

            if (fieldId == 0)
            {
                break;
            }

            switch (fieldId)
            {
                case 2: cipherId = fieldData; break;
                case 3: compressionFlags = BitConverter.ToUInt32(fieldData, 0); break;
                case 4: masterSeed = fieldData; break;
                case 7: encryptionIv = fieldData; break;
                case 11: kdfParameters = VariantDictionary.Read(fieldData); break;
            }
        }

        if (cipherId is null || masterSeed is null || encryptionIv is null || kdfParameters is null)
        {
            throw new KdbxFormatException("En-tête KDBX incomplet.");
        }

        if (new Guid(cipherId) != Aes256CipherUuid)
        {
            throw new KdbxFormatException("Seul le chiffrement AES256 est supporté.");
        }

        var headerBytes = fileBytes[..offset];
        var headerHash = fileBytes[offset..(offset + 32)];
        offset += 32;
        var headerHmac = fileBytes[offset..(offset + 32)];
        offset += 32;

        if (!SHA256Matches(headerBytes, headerHash))
        {
            throw new KdbxFormatException("En-tête KDBX corrompu (hachage SHA-256 invalide).");
        }

        var compositeKey = KdbxCrypto.DeriveCompositeKey(password);
        var transformedKey = KdbxCrypto.TransformKey(compositeKey, kdfParameters);
        var finalKey = KdbxCrypto.DeriveFinalKey(masterSeed, transformedKey);
        var hmacKeyBase = KdbxCrypto.DeriveHmacKeyBase(masterSeed, transformedKey);

        KdbxCrypto.VerifyHeaderHmac(headerBytes, headerHmac, hmacKeyBase);

        using var remainingStream = new MemoryStream(fileBytes, offset, fileBytes.Length - offset);
        var ciphertext = KdbxCrypto.ReadHmacBlockStream(remainingStream, hmacKeyBase);

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = finalKey;
        aes.IV = encryptionIv;
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        var payload = compressionFlags == 1 ? Decompress(decrypted) : decrypted;

        return ParseInnerPayload(payload);
    }

    private static bool SHA256Matches(byte[] data, byte[] expectedHash) =>
        System.Security.Cryptography.SHA256.HashData(data).AsSpan().SequenceEqual(expectedHash);

    private static byte[] Decompress(byte[] gzipped)
    {
        using var input = new MemoryStream(gzipped);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static KdbxDocument ParseInnerPayload(byte[] payload)
    {
        var offset = 0;
        uint innerRandomStreamId = 0;
        byte[]? innerRandomStreamKey = null;

        while (true)
        {
            var fieldId = payload[offset];
            offset += 1;
            var fieldLength = BitConverter.ToInt32(payload, offset);
            offset += 4;
            var fieldData = payload[offset..(offset + fieldLength)];
            offset += fieldLength;

            if (fieldId == 0)
            {
                break;
            }

            switch (fieldId)
            {
                case 1: innerRandomStreamId = BitConverter.ToUInt32(fieldData, 0); break;
                case 2: innerRandomStreamKey = fieldData; break;
            }
        }

        if (innerRandomStreamId != 3 || innerRandomStreamKey is null)
        {
            throw new KdbxFormatException("Seul le flux interne ChaCha20 est supporté.");
        }

        var streamKeyHash = System.Security.Cryptography.SHA512.HashData(innerRandomStreamKey);
        var chachaKey = streamKeyHash[..32];
        var chachaNonce = streamKeyHash[32..44];

        var xmlBytes = payload[offset..];
        var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(xmlBytes));

        var counter = 0;
        var rootGroupXml = xml.Root!.Element("Root")!.Element("Group")!;
        var rootGroup = ParseGroup(rootGroupXml, chachaKey, chachaNonce, ref counter);

        return new KdbxDocument { Root = rootGroup };
    }

    private static KdbxGroup ParseGroup(XElement groupXml, byte[] chachaKey, byte[] chachaNonce, ref int protectedValueCounter)
    {
        var group = new KdbxGroup { Name = groupXml.Element("Name")?.Value ?? "" };

        foreach (var childGroupXml in groupXml.Elements("Group"))
        {
            group.Groups.Add(ParseGroup(childGroupXml, chachaKey, chachaNonce, ref protectedValueCounter));
        }

        foreach (var entryXml in groupXml.Elements("Entry"))
        {
            group.Entries.Add(ParseEntry(entryXml, chachaKey, chachaNonce, ref protectedValueCounter));
        }

        return group;
    }

    private static KdbxEntry ParseEntry(XElement entryXml, byte[] chachaKey, byte[] chachaNonce, ref int protectedValueCounter)
    {
        var entry = new KdbxEntry();

        foreach (var stringXml in entryXml.Elements("String"))
        {
            var key = stringXml.Element("Key")!.Value;
            var valueXml = stringXml.Element("Value")!;
            var isProtected = valueXml.Attribute("Protected")?.Value == "True";

            string value;
            if (isProtected)
            {
                var encryptedBytes = Convert.FromBase64String(valueXml.Value);
                var decryptedBytes = ChaCha20Cipher.Apply(chachaKey, chachaNonce, encryptedBytes);
                value = System.Text.Encoding.UTF8.GetString(decryptedBytes);
                protectedValueCounter += 1;
            }
            else
            {
                value = valueXml.Value;
            }

            switch (key)
            {
                case "Title": entry.Title = value; break;
                case "URL": entry.Url = string.IsNullOrEmpty(value) ? null : value; break;
                case "UserName": entry.UserName = string.IsNullOrEmpty(value) ? null : value; break;
                case "Password": entry.Password = value; break;
                case "Notes": entry.Notes = string.IsNullOrEmpty(value) ? null : value; break;
            }
        }

        return entry;
    }
}
```

**Important note on the ChaCha20 keystream being continuous across protected values**: the implementation above calls `ChaCha20Cipher.Apply` independently per protected value, each starting the keystream over from counter 0 — this is **only correct if each call's internal counter restarts at 0 and the stream is NOT meant to continue across values**. The design spec says protected values must be decrypted "in the order in which they appear", which actually means the keystream is **one continuous stream** shared across all protected values in the file, not reset per value. If after Step 3's verification the Password field decrypts to garbage while Title/URL (unprotected) are fine, this is almost certainly the bug — fix by threading a single running `ChaCha20Cipher` byte-offset/counter state across all `ParseEntry`/`ParseGroup` calls instead of calling `Apply` fresh each time (e.g., generate the full keystream once up front sized to the total protected bytes, or extend `ChaCha20Cipher` with a stateful instance variant that tracks its block counter across calls).

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build src/PassManager.Core/PassManager.Core.csproj`
Expected: Build succeeds, 0 errors.

- [ ] **Step 3: Manual verification — decrypt the real fixture end-to-end**

Add this temporarily to `MauiProgram.cs` (same spot as before):

```csharp
#if DEBUG
		var fixturePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "PassManager.Core.Tests", "Fixtures", "DatabaseTest.kdbx");
		if (File.Exists(fixturePath))
		{
			using var fs = File.OpenRead(fixturePath);
			var doc = PassManager.Core.KeePass.KdbxReader.Read(fs, "Semeru1234@2026");
			void Dump(PassManager.Core.KeePass.KdbxGroup g, int depth)
			{
				System.Diagnostics.Debug.WriteLine($"{new string(' ', depth * 2)}[Group] {g.Name}");
				foreach (var e in g.Entries)
				{
					System.Diagnostics.Debug.WriteLine($"{new string(' ', depth * 2)}  [Entry] Title={e.Title} User={e.UserName} Pass={e.Password} Url={e.Url}");
				}
				foreach (var child in g.Groups)
				{
					Dump(child, depth + 1);
				}
			}
			Dump(doc.Root, 0);
		}
#endif
```

Run: `dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0`, launch, check Debug Output (View → Output → Debug in an attached debugger, or swap to `Console.WriteLine` and run the exe from a terminal if Debug Output isn't visible).
Expected: no exception, and the dumped group/entry names, usernames, and passwords are **readable plaintext that matches what's actually in `DatabaseTest.kdbx`** — open the file in the real KeePass 2.61.1 install (`C:\PortableApps\KeePass-2.61.1\`) with password `Semeru1234@2026` to compare. If an exception is thrown or passwords look like binary garbage, use `superpowers:systematic-debugging`: add `Debug.WriteLine` dumps of `compositeKey`, `transformedKey`, `finalKey` as hex immediately before the HMAC check, and narrow down which stage diverges — the header-level values (cipher/compression/seed/KDF-params/IV) are already confirmed correct per Task 1, so a failure here is in `KdbxCrypto`'s derivation math or the ChaCha20 continuous-stream issue flagged above.

- [ ] **Step 4: Remove the temporary verification block**

Delete the `#if DEBUG ... #endif` block. Rebuild to confirm clean.

- [ ] **Step 5: Commit**

```bash
git add src/PassManager.Core/KeePass/KdbxReader.cs
git commit -m "feat: add KdbxReader — full KDBX4 decrypt and parse pipeline"
```

---

### Task 5: KdbxWriter

**Files:**
- Create: `src/PassManager.Core/KeePass/KdbxWriter.cs`
- Modify: `src/PassManager.Core/KeePass/KdbxCrypto.cs` (add the HMAC block-stream *write* half, symmetric to Task 3's read half)

**Interfaces:**
- Consumes: `KdbxDocument`/`KdbxGroup`/`KdbxEntry` (Task 1), `VariantDictionary.Write` (Task 1), `KdbxCrypto.*` (Task 3), `ChaCha20Cipher.Apply` (Task 2).
- Produces: `KdbxWriter.Write(Stream output, KdbxDocument document, string password)` — consumed by Task 6 (`KdbxExportService`) and the MAUI export flow (Task 7). Always writes using Argon2id (never AES-KDF — picking one simplifies the writer; AES-KDF read-support exists only to open files others created with it).

- [ ] **Step 1: Add the HMAC block-stream writer to KdbxCrypto**

Add this method to the `KdbxCrypto` class from Task 3 (anywhere after `ReadHmacBlockStream`):

```csharp
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
```

- [ ] **Step 2: Implement KdbxWriter**

```csharp
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace PassManager.Core.KeePass;

public static class KdbxWriter
{
    private static readonly Guid Aes256CipherUuid = Guid.Parse("31c1f2e6-bf71-4350-be58-05216afc5aff");
    private static readonly Guid Argon2idUuid = Guid.Parse("9e298b19-56db-4773-b23d-fc3ec6f0a1e6");

    public static void Write(Stream output, KdbxDocument document, string password)
    {
        var masterSeed = RandomNumberGenerator.GetBytes(32);
        var encryptionIv = RandomNumberGenerator.GetBytes(16);
        var kdfSalt = RandomNumberGenerator.GetBytes(32);
        var innerRandomStreamKey = RandomNumberGenerator.GetBytes(64);

        var kdfParameters = new Dictionary<string, object>
        {
            ["$UUID"] = Argon2idUuid.ToByteArray(),
            ["S"] = kdfSalt,
            ["I"] = (ulong)3,
            ["M"] = (ulong)(64 * 1024 * 1024),
            ["P"] = (uint)2,
            ["V"] = (uint)0x13
        };
        var kdfParametersBytes = VariantDictionary.Write(kdfParameters);

        using var headerStream = new MemoryStream();
        WriteUInt32(headerStream, 0x9AA2D903);
        WriteUInt32(headerStream, 0xB54BFB67);
        headerStream.Write(BitConverter.GetBytes((ushort)0));
        headerStream.Write(BitConverter.GetBytes((ushort)4));
        WriteField(headerStream, 2, Aes256CipherUuid.ToByteArray());
        WriteField(headerStream, 3, BitConverter.GetBytes((uint)1));
        WriteField(headerStream, 4, masterSeed);
        WriteField(headerStream, 7, encryptionIv);
        WriteField(headerStream, 11, kdfParametersBytes);
        WriteField(headerStream, 0, [0x0D, 0x0A, 0x0D, 0x0A]);
        var headerBytes = headerStream.ToArray();

        var compositeKey = KdbxCrypto.DeriveCompositeKey(password);
        var transformedKey = KdbxCrypto.TransformKey(compositeKey, kdfParameters);
        var finalKey = KdbxCrypto.DeriveFinalKey(masterSeed, transformedKey);
        var hmacKeyBase = KdbxCrypto.DeriveHmacKeyBase(masterSeed, transformedKey);
        var headerHash = SHA256.HashData(headerBytes);
        var headerHmac = KdbxCrypto.ComputeHeaderHmac(headerBytes, hmacKeyBase);

        var streamKeyHash = SHA512.HashData(innerRandomStreamKey);
        var chachaKey = streamKeyHash[..32];
        var chachaNonce = streamKeyHash[32..44];

        var innerHeaderAndXml = BuildInnerPayload(document, innerRandomStreamKey, chachaKey, chachaNonce);
        var compressed = Compress(innerHeaderAndXml);

        using var aes = Aes.Create();
        aes.Key = finalKey;
        aes.IV = encryptionIv;
        aes.Mode = CipherMode.CBC;
        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(compressed, 0, compressed.Length);

        output.Write(headerBytes, 0, headerBytes.Length);
        output.Write(headerHash, 0, 32);
        output.Write(headerHmac, 0, 32);
        KdbxCrypto.WriteHmacBlockStream(output, ciphertext, hmacKeyBase);
    }

    private static void WriteUInt32(Stream stream, uint value) => stream.Write(BitConverter.GetBytes(value));

    private static void WriteField(Stream stream, byte fieldId, byte[] data)
    {
        stream.WriteByte(fieldId);
        stream.Write(BitConverter.GetBytes(data.Length));
        stream.Write(data);
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static byte[] BuildInnerPayload(KdbxDocument document, byte[] innerRandomStreamKey, byte[] chachaKey, byte[] chachaNonce)
    {
        using var inner = new MemoryStream();
        WriteField(inner, 1, BitConverter.GetBytes((uint)3));
        WriteField(inner, 2, innerRandomStreamKey);
        WriteField(inner, 0, []);

        var xml = BuildXml(document, chachaKey, chachaNonce);
        var xmlBytes = System.Text.Encoding.UTF8.GetBytes(xml.ToString());
        inner.Write(xmlBytes, 0, xmlBytes.Length);

        return inner.ToArray();
    }

    private static XDocument BuildXml(KdbxDocument document, byte[] chachaKey, byte[] chachaNonce)
    {
        var meta = new XElement("Meta",
            new XElement("Generator", "PassManager"),
            new XElement("DatabaseName", "PassManager export"),
            new XElement("RecycleBinEnabled", "False"));

        var rootGroupXml = BuildGroupXml(document.Root, chachaKey, chachaNonce);

        var root = new XElement("KeePassFile", meta, new XElement("Root", rootGroupXml));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static XElement BuildGroupXml(KdbxGroup group, byte[] chachaKey, byte[] chachaNonce)
    {
        var element = new XElement("Group", new XElement("Name", group.Name));

        foreach (var entry in group.Entries)
        {
            element.Add(BuildEntryXml(entry, chachaKey, chachaNonce));
        }

        foreach (var child in group.Groups)
        {
            element.Add(BuildGroupXml(child, chachaKey, chachaNonce));
        }

        return element;
    }

    private static XElement BuildEntryXml(KdbxEntry entry, byte[] chachaKey, byte[] chachaNonce)
    {
        XElement StringField(string key, string? value, bool isProtected)
        {
            var valueElement = new XElement("Value", value ?? "");
            if (isProtected)
            {
                var plainBytes = System.Text.Encoding.UTF8.GetBytes(value ?? "");
                var encryptedBytes = ChaCha20Cipher.Apply(chachaKey, chachaNonce, plainBytes);
                valueElement.Value = Convert.ToBase64String(encryptedBytes);
                valueElement.SetAttributeValue("Protected", "True");
            }

            return new XElement("String", new XElement("Key", key), valueElement);
        }

        return new XElement("Entry",
            StringField("Title", entry.Title, isProtected: false),
            StringField("URL", entry.Url, isProtected: false),
            StringField("UserName", entry.UserName, isProtected: false),
            StringField("Password", entry.Password, isProtected: true),
            StringField("Notes", entry.Notes, isProtected: false));
    }
}
```

**Same continuous-keystream caveat as Task 4 applies here** — if Task 4's fix threads a stateful ChaCha20 counter across values, this writer must use the identical stateful approach (same order of fields/entries/groups as the reader walks them) so encryption and decryption consume the same keystream positions.

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build src/PassManager.Core/PassManager.Core.csproj`
Expected: Build succeeds, 0 errors.

- [ ] **Step 4: Manual verification — round-trip through this app's own reader/writer**

Temporarily add to `MauiProgram.cs`:

```csharp
#if DEBUG
		var doc = new PassManager.Core.KeePass.KdbxDocument
		{
			Root = new PassManager.Core.KeePass.KdbxGroup
			{
				Name = "Root",
				Entries = { new PassManager.Core.KeePass.KdbxEntry { Title = "Test", UserName = "user1", Password = "p@ss!", Url = "https://example.com", Notes = "memo" } }
			}
		};
		using var ms = new MemoryStream();
		PassManager.Core.KeePass.KdbxWriter.Write(ms, doc, "roundtrip-password");
		ms.Position = 0;
		var reread = PassManager.Core.KeePass.KdbxReader.Read(ms, "roundtrip-password");
		var entry = reread.Root.Entries[0];
		System.Diagnostics.Debug.WriteLine($"Round-trip: Title={entry.Title} User={entry.UserName} Pass={entry.Password} Url={entry.Url} Notes={entry.Notes}");
#endif
```

Run, check output.
Expected: `Round-trip: Title=Test User=user1 Pass=p@ss! Url=https://example.com Notes=memo` — every field exactly matches what was written. If this fails but Task 4's real-fixture read succeeded, the bug is specific to `KdbxWriter`. If this also fails intermittently depending on field order, it's almost certainly the continuous-keystream issue.

Add a second entry to the same test group with optional fields left `null` (`new KdbxEntry { Title = "NoOptional", Password = "x" }` — `Url`/`UserName`/`Notes` all default to `null`), re-run, and confirm the re-read entry's `Url`/`UserName`/`Notes` come back as `null`, not the literal string `""` or `"null"`.

- [ ] **Step 5: Remove the temporary verification block**

Delete the `#if DEBUG ... #endif` block. Rebuild to confirm clean.

- [ ] **Step 6: Commit**

```bash
git add src/PassManager.Core/KeePass/KdbxWriter.cs src/PassManager.Core/KeePass/KdbxCrypto.cs
git commit -m "feat: add KdbxWriter — KDBX4 encrypt and serialize pipeline"
```

---

### Task 6: KdbxImportService and KdbxExportService

**Files:**
- Create: `src/PassManager.Core/KeePass/KdbxImportService.cs`
- Create: `src/PassManager.Core/KeePass/KdbxExportService.cs`

**Interfaces:**
- Consumes: `VaultRepository.CreateFolder(Guid parentId, string name) -> VaultFolder`, `VaultRepository.CreateEntry(Guid folderId, string title, string? url, string? login, string password, string? memo) -> VaultEntry`, `VaultRepository.GetRootFolder() -> VaultFolder?`, `VaultRepository.GetChildFolders(Guid? parentId) -> IReadOnlyList<VaultFolder>`, `VaultRepository.GetEntries(Guid folderId) -> IReadOnlyList<VaultEntry>` (all existing, in `src/PassManager.Core/Vault/VaultRepository.cs`), `IClock.UtcNow` (existing, `src/PassManager.Core/Abstractions/IClock.cs`), `KdbxDocument`/`KdbxGroup`/`KdbxEntry` (Task 1).
- Produces: `KdbxImportService.Import(VaultRepository vault, KdbxDocument document, IClock clock)`, `KdbxExportService.Export(VaultRepository vault) -> KdbxDocument` — both consumed by the MAUI UI (Task 7).

- [ ] **Step 1: Implement KdbxImportService**

```csharp
using PassManager.Core.Abstractions;
using PassManager.Core.Vault;

namespace PassManager.Core.KeePass;

public static class KdbxImportService
{
    public static void Import(VaultRepository vault, KdbxDocument document, IClock clock)
    {
        var root = vault.GetRootFolder() ?? throw new InvalidOperationException("Coffre sans dossier racine.");
        var importFolder = vault.CreateFolder(root.Id, $"Import KeePass – {clock.UtcNow:yyyy-MM-dd}");

        ImportGroup(vault, document.Root, importFolder.Id);
    }

    private static void ImportGroup(VaultRepository vault, KdbxGroup group, Guid parentFolderId)
    {
        foreach (var entry in group.Entries)
        {
            vault.CreateEntry(parentFolderId, entry.Title, entry.Url, entry.UserName, entry.Password, entry.Notes);
        }

        foreach (var childGroup in group.Groups)
        {
            var childFolder = vault.CreateFolder(parentFolderId, childGroup.Name);
            ImportGroup(vault, childGroup, childFolder.Id);
        }
    }
}
```

- [ ] **Step 2: Implement KdbxExportService**

```csharp
using PassManager.Core.Vault;

namespace PassManager.Core.KeePass;

public static class KdbxExportService
{
    public static KdbxDocument Export(VaultRepository vault)
    {
        var root = vault.GetRootFolder() ?? throw new InvalidOperationException("Coffre sans dossier racine.");
        return new KdbxDocument { Root = ExportFolder(vault, root.Id, root.Name) };
    }

    private static KdbxGroup ExportFolder(VaultRepository vault, Guid folderId, string name)
    {
        var group = new KdbxGroup { Name = name };

        foreach (var entry in vault.GetEntries(folderId))
        {
            group.Entries.Add(new KdbxEntry
            {
                Title = entry.Title,
                Url = entry.Url,
                UserName = entry.Login,
                Password = entry.Password,
                Notes = entry.Memo
            });
        }

        foreach (var childFolder in vault.GetChildFolders(folderId))
        {
            group.Groups.Add(ExportFolder(vault, childFolder.Id, childFolder.Name));
        }

        return group;
    }
}
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build src/PassManager.Core/PassManager.Core.csproj`
Expected: Build succeeds, 0 errors. If `VaultRepository.GetChildFolders`/`GetRootFolder` signatures differ from what's used above, check `src/PassManager.Core/Vault/VaultRepository.cs` and adjust the calls to match (these methods already exist — see the Interfaces block).

- [ ] **Step 4: Commit**

```bash
git add src/PassManager.Core/KeePass/KdbxImportService.cs src/PassManager.Core/KeePass/KdbxExportService.cs
git commit -m "feat: add KdbxImportService and KdbxExportService (vault <-> KDBX mapping)"
```

---

### Task 7: MAUI UI wiring (Import/Export menu)

**Files:**
- Modify: `src/PassManager.Maui/ViewModels/VaultTreeViewModel.cs`
- Modify: `src/PassManager.Maui/Views/VaultTreePage.xaml`

**Interfaces:**
- Consumes: `KdbxReader.Read`/`KdbxWriter.Write` (Tasks 4/5), `KdbxImportService.Import`/`KdbxExportService.Export` (Task 6), `AuthSessionService.Vault`/`SaveVaultAsync` (existing), `Microsoft.Maui.Storage.FilePicker.PickAsync` (built-in MAUI Essentials), `CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync` (existing dependency — check its exact method signature in whatever `.cs` file in this codebase already uses `FileSaver`, if any; otherwise its signature is `Task<FileSaverResult> SaveAsync(string fileName, Stream stream, CancellationToken ct = default)`).
- Produces: `VaultTreeViewModel.ImportExportCommand` bound from `VaultTreePage.xaml`.

- [ ] **Step 1: Add the ImportExportCommand to VaultTreeViewModel**

Add this method to `VaultTreeViewModel` (anywhere alongside the other `[RelayCommand]` methods — e.g. right after `EntryOptionsAsync`):

```csharp
    [RelayCommand]
    private async Task ImportExportAsync()
    {
        var vault = authSession.Vault;
        if (vault is null)
        {
            return;
        }

        var page = Shell.Current.CurrentPage;
        var action = await page.DisplayActionSheet("Dossier", "Annuler", null, "Importer (.kdbx)", "Exporter (.kdbx)");

        switch (action)
        {
            case "Importer (.kdbx)":
                await ImportKdbxAsync(vault, page);
                break;

            case "Exporter (.kdbx)":
                await ExportKdbxAsync(vault, page);
                break;
        }
    }

    private async Task ImportKdbxAsync(PassManager.Core.Vault.VaultRepository vault, Page page)
    {
        FileResult? pickedFile;
        try
        {
            pickedFile = await FilePicker.Default.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".kdbx" }
                }),
                PickerTitle = "Choisir un fichier KeePass (.kdbx)"
            });
        }
        catch (Exception)
        {
            return;
        }

        if (pickedFile is null)
        {
            return;
        }

        var password = await page.DisplayPromptAsync("Mot de passe", "Mot de passe du fichier KeePass", "Importer", "Annuler");
        if (password is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            using var stream = await pickedFile.OpenReadAsync();
            var document = await Task.Run(() => PassManager.Core.KeePass.KdbxReader.Read(stream, password));
            var countBefore = Entries.Count;
            PassManager.Core.KeePass.KdbxImportService.Import(vault, document, new PassManager.Core.Common.SystemClock());
            await authSession.SaveVaultAsync();
            RebuildTree();
            StatusMessage = "Import KeePass terminé.";
        }
        catch (PassManager.Core.KeePass.KdbxFormatException)
        {
            ErrorMessage = "Mot de passe incorrect ou fichier invalide.";
        }
        catch (Exception)
        {
            ErrorMessage = "Échec de l'import.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportKdbxAsync(PassManager.Core.Vault.VaultRepository vault, Page page)
    {
        var password = await page.DisplayPromptAsync("Mot de passe", "Nouveau mot de passe pour le fichier exporté", "Suivant", "Annuler");
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        var confirmPassword = await page.DisplayPromptAsync("Confirmation", "Confirmez le mot de passe", "Exporter", "Annuler");
        if (confirmPassword != password)
        {
            ErrorMessage = "Les mots de passe ne correspondent pas.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var document = PassManager.Core.KeePass.KdbxExportService.Export(vault);
            using var stream = new MemoryStream();
            await Task.Run(() => PassManager.Core.KeePass.KdbxWriter.Write(stream, document, password));
            stream.Position = 0;

            var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync("export.kdbx", stream);
            if (result.IsSuccessful)
            {
                StatusMessage = "Export KeePass terminé.";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "Échec de l'export.";
        }
        finally
        {
            IsBusy = false;
        }
    }
```

Add these `using` statements to the top of `VaultTreeViewModel.cs` if not already present: `using Microsoft.Maui.Storage;` (for `FilePicker`/`FileResult`/`PickOptions`/`FilePickerFileType`).

Check `src/PassManager.Core/Abstractions/IClock.cs` and whatever class implements it for the app (likely `SystemClock` somewhere under `PassManager.Core` or `PassManager.Maui`) — adjust the `new PassManager.Core.Common.SystemClock()` call above to the real namespace/type if it differs (search the codebase for `: IClock` to find it).

- [ ] **Step 2: Add the menu button to VaultTreePage.xaml**

In `src/PassManager.Maui/Views/VaultTreePage.xaml`, find the top bar's `HorizontalStackLayout` (containing "Synchroniser"/"Déconnexion") and add a new button before it:

```xml
                <Button Text="⋯" WidthRequest="44" Command="{Binding ImportExportCommand}"
                        BackgroundColor="Transparent" TextColor="{StaticResource Gray500}" />
```

placed as the first child inside the existing `<HorizontalStackLayout Grid.Column="2" Spacing="8">` (so it reads: ⋯ menu, then Synchroniser, then Déconnexion).

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0`
Expected: Build succeeds, 0 errors. Fix any namespace/API mismatches found (`FileSaver` exact method name, `IClock` implementation type) by searching the codebase as instructed above.

- [ ] **Step 4: Commit**

```bash
git add src/PassManager.Maui/ViewModels/VaultTreeViewModel.cs src/PassManager.Maui/Views/VaultTreePage.xaml
git commit -m "feat: wire KeePass import/export into the vault screen's ⋯ menu"
```

---

### Task 8: End-to-end manual verification

**Files:** none (verification only).

**Interfaces:** none.

- [ ] **Step 1: Rebuild and launch the client**

```bash
dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0
```

Launch `src/PassManager.Maui/bin/Debug/net10.0-windows10.0.19041.0/win-x64/PassManager.Maui.exe`, log in.

- [ ] **Step 2: Import the real fixture through the UI**

Click the new "⋯" menu → "Importer (.kdbx)" → pick `src/PassManager.Core.Tests/Fixtures/DatabaseTest.kdbx` → enter password `Semeru1234@2026`.
Expected: a new "Import KeePass – {today's date}" folder appears in the tree, containing the same groups/entries you confirmed in Task 4's manual check (cross-reference against opening the file in actual KeePass 2.61.1 if you haven't already memorized its contents). Opening one of the imported entries in `EntryDetailPage` must show the correct title/login/password/URL.

Then repeat the import with the correct file but a deliberately wrong password (e.g. `wrong-password`).
Expected: `ErrorMessage` shows "Mot de passe incorrect ou fichier invalide." — no folder is created, no partial data appears in the tree.

- [ ] **Step 3: Export the vault and re-import it**

Click "⋯" → "Exporter (.kdbx)" → enter a password twice → save the file (note the path FileSaver reports). Then click "⋯" → "Importer (.kdbx)" again, pick the file you just exported, enter the same password.
Expected: a second new "Import KeePass – {today's date}" folder appears, containing everything that was in the vault at export time (including the first import's contents, since export covers the whole vault) — confirming the writer and reader agree with each other through the real app, not just in the Task 5 in-memory check.

- [ ] **Step 4: Try opening the exported file in real KeePass**

Open the exported `.kdbx` file in the KeePass 2.61.1 install at `C:\PortableApps\KeePass-2.61.1\` with the password you chose during export.
Expected: KeePass opens it without error and shows the same entries. If KeePass rejects the file or shows an error, the `Meta` element built in `KdbxWriter.BuildXml` is likely missing a field real KeePass requires — inspect the error message KeePass shows and adjust `BuildXml` accordingly; this is a lower-risk, iteratively-fixable issue compared to the crypto layer, since XML structure problems surface immediately and specifically.

- [ ] **Step 5: Confirm cancel paths show no error**

Click "⋯" → "Importer (.kdbx)" → cancel the file picker dialog without selecting a file.
Expected: no `ErrorMessage` appears (silent no-op), confirming the cancel path from Review Focus is handled correctly.
