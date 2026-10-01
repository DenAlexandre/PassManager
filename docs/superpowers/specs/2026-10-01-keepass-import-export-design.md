# KeePass (.kdbx) import/export — design spec

Date: 2026-10-01

## Context and goal

PassManager's local vault is already "a proprietary format, not real
`.kdbx` — inspired by KeePass concepts... but not binary-compatible with
the KeePass app" (CLAUDE.md). This spec adds genuine interoperability on
top of that: a user can import an existing KeePass database into their
PassManager vault, and export their PassManager vault as a real `.kdbx`
file readable by KeePass/KeePassXC. This is used regularly in both
directions (not a one-time migration) — confirmed during design.

## Scope

- **Format: KDBX4 only.** No KDBX3.1 support (older AES-KDF format).
  KDBX4 is the default export format of KeePass (since 2.39, 2017) and
  KeePassXC.
- **Outer cipher: AES256-CBC only.** Reject files that specify
  ChaCha20 or TwoFish as the outer cipher with a clear "unsupported
  cipher" error — rare in practice.
- **KDF: AES-KDF, Argon2d, or Argon2id.** Argon2 variants use the
  `Konscious.Security.Cryptography.Argon2` package already referenced by
  `PassManager.Core` (used today for the app's own local vault-key
  derivation). AES-KDF needs no extra dependency — it's repeated
  AES-256-ECB block encryption, built on .NET's own `System.Security.Cryptography.Aes`.
  Scope revised during planning: the real KeePass 2.61.1 test fixture
  (`DatabaseTest.kdbx`, see Testing) turned out to use AES-KDF, not
  Argon2 — confirmed by inspecting its `KdfParameters` bytes directly
  (`$UUID` = `C9D9F39A-628A-4460-BF74-0D08C18A4FEA`). AES-KDF is still
  the long-standing historical default for KeePass databases that
  haven't been migrated to Argon2, so supporting it is as important in
  practice as Argon2. Any other KDF UUID is rejected with a clear error.
- **Inner protected-value stream: ChaCha20 only** (`InnerRandomStreamID`
  = 3). Reject Salsa20 (`InnerRandomStreamID` = 2, the older default)
  with a clear error — modern KeePass/KeePassXC always write ChaCha20
  for KDBX4.
- **Fields carried: Title, URL, UserName, Password, Notes** — mapped to
  `VaultEntry.Title/Url/Login/Password/Memo`. Everything else KDBX can
  hold (attachments, custom icons, custom string fields, tags, expiry,
  TOTP seeds, entry history, recycle bin) is **silently dropped on
  import and never emitted on export.** This is an explicit,
  confirmed tradeoff — PassManager's own data model does not have
  equivalent fields and extending it is out of scope for this feature.
- **No external KeePass library dependency.** All parsing/writing is
  implemented from scratch in `PassManager.Core`, reusing only the
  already-referenced Argon2 package and .NET's built-in
  `System.Security.Cryptography` (AES, HMACSHA256) and
  `System.IO.Compression` (GZip). Rationale: every mature .NET KeePass
  library available today derives from KeePassLib (GPL-licensed) —
  this repo has no LICENSE file (private project), and taking on a
  copyleft dependency now would constrain any future licensing choice.
  Writing our own is consistent with the project's existing stance of
  implementing its own crypto/file-format code rather than taking on
  a heavy dependency (confirmed during design).

## KDBX4 format reference (for implementers)

An outer cleartext header (magic signature `0x9AA2D903` / `0xB54BFB67`,
then a 4-byte version — major must be `>= 4`), followed by a sequence
of type-length-value fields terminated by an "end of header" marker.
The fields this implementation reads/writes:
- `CipherID` (16 bytes): must equal the AES256 cipher UUID
  `31C1F2E6-BF71-4350-BE58-05216AFC5AFF`.
- `CompressionFlags` (4 bytes, uint32): `0` = none, `1` = gzip. Both
  supported on read; write always uses gzip.
- `MasterSeed` (32 bytes): random, written fresh on every export.
- `EncryptionIV` (16 bytes): random, written fresh on every export.
- `KdfParameters`: a binary `VariantDictionary` (a type-tagged
  key→value map; entries are `byte Type, UInt32 KeyLength, byte[] Key,
  UInt32 ValueLength, byte[] Value`, terminated by a `Type == 0` byte).
  Confirmed against the real `DatabaseTest.kdbx` fixture byte-for-byte:
  `$UUID` (Type `0x42` ByteArray, 16 bytes) holds the KDF UUID — one of
  AES-KDF `C9D9F39A-628A-4460-BF74-0D08C18A4FEA`, Argon2d
  `EF636DDF-8C29-444B-91F7-A9A403E30A0C`, or Argon2id
  `9E298B19-56DB-4773-B23D-FC3EC6F0A1E6`. For AES-KDF: `S` (Type `0x42`
  ByteArray, 32-byte transform seed), `R` (Type `0x05` UInt64, transform
  rounds — `600000` in the fixture). For Argon2d/id: `S` (salt), `I`
  (iterations, UInt64), `M` (memory in bytes, UInt64), `P` (parallelism,
  UInt32), `V` (KDF version, UInt32).

After the outer header: a 32-byte SHA-256 hash of the header bytes
(corruption check, not secret), then a 32-byte HMAC-SHA256 of the
header (authentication, keyed from the composite key — detects wrong
password before doing any expensive decryption), then a sequence of
HMAC-authenticated blocks (`HMAC-SHA256(32 bytes) + length(uint32) +
data`, terminated by a zero-length block) whose concatenated data is
the AES-256-CBC ciphertext.

Key derivation: `CompositeKey = SHA256(SHA256(UTF8(password)))` (no
keyfile support — password-only databases only, confirmed in scope).
Then, depending on the KDF UUID:
- **AES-KDF**: `TransformedKey = CompositeKey` (32 bytes) to start;
  repeat `R` times: encrypt the 32-byte value as two independent
  16-byte blocks with raw AES-256-ECB (no padding, no chaining between
  the two blocks) using `S` as the 256-bit key, replacing the value
  with the ciphertext each round. After `R` rounds, `TransformedKey =
  SHA256(result)`.
- **Argon2d/Argon2id**: `TransformedKey = Argon2(CompositeKey,
  salt=S, iterations=I, memoryKiB=M/1024, parallelism=P, hashLength=32,
  variant=Argon2d-or-Argon2id-per-UUID)`.

Then in both cases: `FinalKey = SHA256(MasterSeed || TransformedKey)`.
The HMAC key for block authentication is derived similarly but hashed
with an additional counter/offset per the published algorithm —
implementers should follow the reference description (e.g. the KeePass
source or community format writeups) exactly here; this is the easiest
place to get subtly wrong. Because the real fixture file's header
bytes (cipher/compression/seed/KDF params/IV) have already been
verified by hand against this implementation's intended parsing logic
(see Testing), a working implementation that fails to decrypt
`DatabaseTest.kdbx` almost certainly has a bug in this key-derivation
or HMAC-keying step, not in header parsing.

Decrypting with `FinalKey`/`EncryptionIV` and gzip-decompressing
yields: an **inner header** (its own small TLV sequence — only field
that matters here: `InnerRandomStreamID` = 4-byte uint32, must be `3`
for ChaCha20, and `InnerRandomStreamKey` = the key for that stream),
followed by the KeePass inner XML (`KeePassFile > Root > Group`,
recursively nested, each `Group` having a `Name` and child `Group`/
`Entry` elements; each `Entry` has `String` child elements with a
`Key` and `Value`, where `Value` may carry a `Protected="True"`
attribute meaning its text is Base64 of bytes XORed against the
running ChaCha20 keystream, consumed in document order across all
protected values in the file).

## Components

All new code lives in `src/PassManager.Core/KeePass/` — no MAUI
reference, consistent with `PassManager.Core`'s existing
headless-testable design.

- **`ChaCha20Cipher.cs`** — a minimal raw ChaCha20 keystream generator
  (RFC 7539 construction: 4 constants + 256-bit key + 32-bit counter +
  96-bit nonce → 20-round block function → XOR with plaintext/
  ciphertext). Not the same as `System.Security.Cryptography.ChaCha20Poly1305`,
  which is AEAD-only and cannot produce a raw keystream for this use.
- **`VariantDictionary.cs`** — `Read(byte[]) -> Dictionary<string, object>`
  and `Write(Dictionary<string, object>) -> byte[]` for the
  `KdfParameters` binary TLV blob.
- **`KdbxCrypto.cs`** — composite-key derivation, Argon2 transform
  (via `Konscious.Security.Cryptography.Argon2d`/`Argon2id`), final AES
  key derivation, HMAC-block-stream reader and writer.
- **`KdbxReader.cs`** — `public static KdbxDocument Read(Stream kdbxFile, string password)`.
  Throws `KdbxFormatException` for every failure mode (bad signature,
  unsupported version/cipher/KDF/inner-stream, HMAC mismatch — which
  covers both "wrong password" and "corrupted file" — malformed XML).
- **`KdbxWriter.cs`** — `public static void Write(Stream output, KdbxDocument document, string password)`.
  Generates fresh random `MasterSeed`/`EncryptionIV`/KDF salt/inner
  stream key on every call — never reuses randomness across exports.
- **`KdbxDocument.cs`** — the in-memory model produced by the reader
  and consumed by the writer:
  ```csharp
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
  Deliberately simpler than `VaultFolder`/`VaultEntry` — no IDs,
  versions, or timestamps; those are assigned when mapping into/out of
  the live vault.
- **`KdbxFormatException.cs`** — single exception type for every
  parse/crypto failure. The UI layer treats all of these identically
  (one generic error message) — not distinguishing "wrong password"
  from "corrupted file" to the user is standard practice and avoids
  leaking information.
- **`KdbxImportService.cs`** — `public static void Import(VaultRepository vault, KdbxDocument document, IClock clock)`:
  creates one new top-level folder under the vault's root, named
  `"Import KeePass – {clock.UtcNow:yyyy-MM-dd}"`, then recursively
  walks `document.Root.Groups`/`.Entries`, calling the existing
  `VaultRepository.CreateFolder`/`CreateEntry` (which already generate
  fresh `Guid`s internally — confirmed, no ID-collision handling
  needed). Never merges into existing folders, matching the
  already-established "always fresh copy, no merge" convention from
  folder/entry sharing.
- **`KdbxExportService.cs`** — `public static KdbxDocument Export(VaultRepository vault)`:
  walks the live folder tree from `vault.GetRootFolder()` down,
  building the mirror `KdbxDocument` tree (root folder's children
  become the top-level groups — KeePass has no "root folder" concept
  of its own, the inner XML's `Root` group IS the database root).

## Client (`PassManager.Maui`)

- **`VaultTreeViewModel`**: new `ImportExportCommand` →
  `Shell.Current.CurrentPage.DisplayActionSheet("Dossier", "Annuler", null, "Importer (.kdbx)", "Exporter (.kdbx)")`.
  - **Import branch**: `FilePicker.PickAsync(new PickOptions { FileTypes = ... })`
    filtered to `.kdbx` (built into MAUI Essentials — no new
    dependency) → `DisplayPromptAsync` for the file's master password
    → `await Task.Run(() => KdbxReader.Read(stream, password))` (off
    the UI thread — Argon2 with real-world parameters takes real wall
    time) → on success, `KdbxImportService.Import(vault, document, clock)`
    → `SaveVaultAsync()` → `RebuildTree()` → `StatusMessage` showing
    the imported entry count. On `KdbxFormatException`, `ErrorMessage`
    = "Mot de passe incorrect ou fichier invalide."
  - **Export branch**: `DisplayPromptAsync` for a new master password,
    then a second `DisplayPromptAsync` to confirm it (mirrors
    `RegisterPage`'s password/confirm-password pattern) — mismatch
    shows an error and aborts before touching the file system →
    `Task.Run(() => KdbxExportService.Export(vault))` then
    `Task.Run(() => KdbxWriter.Write(stream, document, password))` →
    `CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(fileName, stream)`
    (already an existing dependency, pinned per CLAUDE.md's known
    pitfalls — no version bump needed for this feature) lets the user
    pick the destination. A user-cancelled file picker/saver is not an
    error (no `ErrorMessage` shown); a genuine I/O failure is.

## Error handling

- Every `KdbxReader`/`KdbxWriter` failure is a `KdbxFormatException` —
  never a raw exception type that could leak implementation detail.
- The ViewModel's catch block distinguishes exactly two outcomes:
  `KdbxFormatException` → the generic incorrect-password/invalid-file
  message; any other exception (I/O, cancellation) → either silently
  ignored (user cancelled the picker) or a generic "Échec de l'export/
  import" message, matching the existing broad-catch pattern already
  used throughout `VaultTreeViewModel`/`ShareViewModel`.

## Verification (no automated tests — explicit decision for this feature)

The user directed that this plan have no automated test suite. Every
step is instead verified manually, by running the actual code against
the real `DatabaseTest.kdbx` fixture (`src/PassManager.Core.Tests/Fixtures/DatabaseTest.kdbx`,
master password `Semeru1234@2026` — test-only credential, no other
use) and inspecting the output. This fixture's outer-header bytes have
already been hand-decoded and confirmed against this spec's parsing
rules (see the format reference above and the plan's Task 1), which
gives concrete expected intermediate values to check against even
without formal assertions:
- `CipherID` → AES256 UUID `31C1F2E6-BF71-4350-BE58-05216AFC5AFF`
- `CompressionFlags` → `1` (gzip)
- `MasterSeed` → 32 bytes starting `18 95 BB FF C2 1A 41 0A...`
- `KdfParameters.$UUID` → AES-KDF UUID `C9D9F39A-628A-4460-BF74-0D08C18A4FEA`
- `KdfParameters.R` (rounds) → `600000`
- `KdfParameters.S` (seed) → 32 bytes starting `53 81 E2 F1 4A D3 4A 07...`
- `EncryptionIV` → 16 bytes starting `EE 05 B8 27 5F 9B C6 EC...`

A build that fails to decrypt this fixture should be debugged with
`superpowers:systematic-debugging` — print the intermediate values
above plus `CompositeKey`/`TransformedKey`/`FinalKey` and compare
against what re-deriving by hand would produce, rather than guessing
at fixes. Given there is no unit-test safety net for this feature,
getting the header parsing right first (verifiable against the exact
bytes above) before moving on to key derivation isolates where a bug
is most likely to be.

Final verification is end-to-end through the running MAUI app: import
`DatabaseTest.kdbx`, confirm its folders/entries appear under a new
"Import KeePass – {date}" folder with correct titles/logins/passwords
(the implementer must open the file in actual KeePass once to know
what to expect, since this spec doesn't enumerate its contents); then
export the vault, then re-import that exported file through the same
UI to confirm the writer/reader round-trip through the real app.
