# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

PassManager is a KeePass-inspired, multi-platform password manager: a .NET MAUI client (Android/iOS/macOS/Windows) backed by an ASP.NET Core API + PostgreSQL, deployed as two Docker containers (`api`, `postgres`) via `infra/docker-compose.yml`, suitable for Portainer.

Key product/architecture decisions (do not relitigate without asking the user — these were explicit tradeoffs):
- **No zero-knowledge encryption.** The server holds an AES-256-GCM master key (`Vault__MasterKey`) and can decrypt entry passwords/memos at rest. This was chosen deliberately so that folder "sharing" between accounts can be a simple server-side copy instead of requiring per-user asymmetric key management.
- **The local vault file is a proprietary format**, not real `.kdbx` — inspired by KeePass concepts (KDF + AEAD) but not binary-compatible with the KeePass app.
- **Auth/vault key separation**: the account password is sent once to the server (TLS) and hashed with `PasswordHasher<User>` for login; separately, the client derives a *different* local vault encryption key via Argon2id from the same password + a server-issued `vaultSalt`. The server never sees or stores anything that can derive the vault key.
- Email confirmation has no real provider wired up yet (`Email__Provider=Console` by default) — confirmation links are logged by `DevConsoleEmailSender` and read from `docker compose logs api`.

## Repository layout

```
backend/src/{Domain,Application,Infrastructure,Api}   # ASP.NET Core API, Clean Architecture layering
backend/tests/{Domain,Application,Api.Integration}.Tests
src/PassManager.Core            # crypto, local vault format, sync engine — NO Microsoft.Maui.* reference, headless-testable
src/PassManager.Core.Tests
src/PassManager.Maui            # MAUI client (Android/iOS/MacCatalyst/Windows), MVVM via CommunityToolkit.Mvvm
infra/docker-compose.yml        # the only 2 deployable services: api + postgres
PassManager.slnx                # solution file (note: .slnx, not .sln)
```

Backend follows strict layering: `Domain` has zero framework dependencies (pure entities/abstractions) → `Application` (business logic, depends only on `Domain` + EF Core *abstractions* via `IPassManagerDbContext`, no Npgsql) → `Infrastructure` (EF Core/Npgsql implementation, crypto, email, JWT) → `Api` (controllers, DI wiring in `Program.cs`). `PassManager.Core` (the client-side library) is an entirely separate, parallel hierarchy — it does not reference anything in `backend/`.

## Build & test commands

```bash
# Build everything
dotnet build PassManager.slnx

# Build/test a single project
dotnet test backend/tests/Application.Tests/PassManager.Application.Tests.csproj
dotnet test src/PassManager.Core.Tests/PassManager.Core.Tests.csproj

# Run a single test
dotnet test backend/tests/Application.Tests/PassManager.Application.Tests.csproj --filter "FullyQualifiedName~SyncServiceTests.PushAsync_NewFolder_IsAcceptedAndPersisted"

# Build the MAUI client for a specific platform (dotnet test does not run on MAUI head projects)
dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0
```

`dotnet test` cannot take multiple `.csproj` paths in one invocation — run each test project separately (or target `PassManager.slnx` to run all of them, slower).

### Backend EF Core migrations

```bash
dotnet ef migrations add <Name> \
  --project backend/src/Infrastructure/PassManager.Infrastructure.csproj \
  --startup-project backend/src/Api/PassManager.Api.csproj \
  --output-dir Persistence/Migrations
```

Migrations apply automatically on API startup when `Database__AutoMigrate=true` (set in `infra/docker-compose.yml` and in `appsettings.Development.json`).

### Running the backend locally

```bash
cp infra/.env.example infra/.env
# edit infra/.env — set POSTGRES_PASSWORD, JWT_SIGNING_KEY, VAULT_MASTER_KEY (base64, 32 bytes: openssl rand -base64 32)
docker compose --env-file infra/.env -f infra/docker-compose.yml up --build
```

API listens on `http://localhost:8080`. To read the dev confirmation-email link: `docker compose -f infra/docker-compose.yml logs api | grep "DEV EMAIL"`.

### Running the MAUI client

Build for Windows as above, then launch `src/PassManager.Maui/bin/Debug/net10.0-windows10.0.19041.0/win-x64/PassManager.Maui.exe`. The client points at `http://localhost:8080/` on non-Android, and `http://10.0.2.2:8080/` on the Android emulator (see `PassManager.Maui/Common/ApiConfig.cs`).

## Backend architecture

- **Entities** (`Domain/Entities`): `User`, `Folder` (self-referencing tree, exactly one `IsRoot` folder per user named "Racine", enforced by a unique filtered index), `Entry` (belongs to a `Folder`), `RefreshToken`, `EmailConfirmationToken`, `FolderShare` (audit log of shares).
- **Envelope encryption**: `Entry.Password`/`Entry.Memo` are encrypted transparently via an EF Core `ValueConverter` (`EntryConfiguration.cs` + `EnvelopeEncryptionService`) using `Vault__MasterKey`. Application/Domain code always sees plaintext; only the DB column is ciphertext.
- **Sync protocol** (`Application/Sync/SyncService.cs`): client-generated UUIDs for folders/entries (so offline creation never collides), a `Version` counter + `UpdatedAt` timestamp per row, and soft-delete (`DeletedAt`) instead of hard deletes. `PushAsync` accepts if `ClientVersion == stored Version`; on mismatch it's last-write-wins by timestamp (client's newer edit still wins even with a stale version) or returns a conflict with the server's row. Deletes of a row the server never saw are a no-op accept, not an insert-then-delete. `PullAsync` is cursor-paginated (500 rows) and the client must advance its cursor to the server-returned `nextCursor`/`serverTime`, never its own clock.
- **Sharing** (`Application/Sharing/FolderSharingService.cs`): deep-copies a folder subtree + its entries into the target user's root folder with fresh IDs; name collisions at the top level get a `" (partagé par {email})"` suffix. Works because the server can decrypt (see above) — no client-side re-encryption needed.
- **Auth** (`Application/Auth/AuthService.cs`): register → auto-creates the root folder + sends (via `IEmailSender`) a confirmation link → login rejected with `EMAIL_NOT_CONFIRMED` until confirmed → JWT access token (15 min) + rotating opaque refresh token. `IEmailSender` has `DevConsoleEmailSender` (default) and `SmtpEmailSender` (MailKit) implementations, switched by `Email__Provider` config.
- Controllers call into `Application` services and translate results to `ErrorResponseDto { code, message }` with endpoint-specific HTTP status codes — follow this pattern for new endpoints rather than throwing raw exceptions.

## Client architecture (PassManager.Core + PassManager.Maui)

- **`.pmvault` file format** (`Core/Vault/VaultFileHeader.cs`, `VaultSerializer.cs`): a 45-byte cleartext header (magic `"PMVL"`, format version, KDF/cipher ids, Argon2 params, salt, nonce) used as AES-GCM **associated data** — tampering with any header field fails authentication on decrypt — followed by a 16-byte tag and the AES-256-GCM ciphertext of the JSON `VaultDocument` (folders/entries/tombstones/syncState).
- **`VaultRepository`** (`Core/Vault/`): in-memory CRUD over a loaded `VaultDocument`. Invariant: a deleted folder/entry is *removed* from the active list and a corresponding `VaultTombstone` (with the item's last-known `Version`, needed for correct server-side conflict resolution) is added — there is no "soft delete" flag on the live models.
- **`SyncEngine`** (`Core/Sync/SyncEngine.cs`): push-then-pull. It pushes local dirty items (by `UpdatedAt > lastSyncUtc`) and tombstones (skipping `Version == 0` tombstones — those were never synced, so there's nothing to tell the server), then immediately pulls from the *pre-push* cursor so the just-pushed rows come back with their authoritative server `Version`/`UpdatedAt` in the same pass — this is why push responses don't need to echo back per-item versions.
- **`AuthSessionService`** (`Core/Auth/`): orchestrates login (derives the vault key, loads/creates the `.pmvault` file, triggers an initial best-effort sync), holds the current `VaultRepository`, and exposes `SaveVaultAsync`/`SyncAsync`/`LogoutAsync`.
- **MAUI client** (`src/PassManager.Maui`): CommunityToolkit.Mvvm ViewModels (`[ObservableProperty]`/`[RelayCommand]`) + Shell navigation. Pages/ViewModels are constructor-injected and registered as `Transient` in `MauiProgram.cs`; cross-cutting services (`AuthSessionService`, `SyncEngine`, HTTP clients) are `Singleton`. There's no first-party MAUI TreeView — the folder tree is a flattened, depth-indented `CollectionView` (`VaultTreeViewModel`/`FolderNodeViewModel`).
- `AuthHeaderHandler` (a `DelegatingHandler` attached to the sync/share `HttpClient`s) attaches the bearer token and retries once after a 401 by calling `/api/auth/refresh`.

## Known pitfalls (hard-won, don't reintroduce)

- **DI cycle**: `AuthHeaderHandler` must depend on `ISecureStorageService` + `IAuthApiClient` directly — **never** on `AuthSessionService`. `AuthSessionService → SyncEngine → ISyncApiClient` and `ISyncApiClient`'s `HttpClient` pipeline includes `AuthHeaderHandler`; if that handler depended on `AuthSessionService` it closes the cycle. This manifests as a native stack-overflow crash at app startup with **zero** managed exception/log output (uncatchable, no Windows Event Log entry) — extremely hard to diagnose from symptoms alone. Shared key-naming lives in `Core/Auth/SessionStorageKeys.cs` so both sides agree without a dependency.
- **EF Core doesn't snake_case column names** by default here — raw-SQL index filters (e.g. `HasFilter("\"IsRoot\"")` in `FolderConfiguration`) must use the actual PascalCase column name, not the conceptual snake_case name from design docs.
- **`CommunityToolkit.Maui` version is pinned to 13.0.0** in `PassManager.Maui.csproj` — newer versions require a `Microsoft.Maui.Controls` version ahead of what the installed `maui-windows` workload resolves (`MauiVersion` 10.0.20), causing an NU1605 downgrade error. If bumping CommunityToolkit.Maui, check its `Microsoft.Maui.Controls` floor first.
- `.UseMauiCommunityToolkit()` must be chained in `MauiProgram.CreateMauiApp()` whenever `CommunityToolkit.Maui` is referenced, or the MCT001 analyzer fails the build.
- `.dockerignore` must exclude `**/bin/`, `**/obj/` — otherwise Windows-built artifacts get copied into the Linux build container and corrupt the restore (`NuGet.Packaging.Core.PackagingException`).
- ASP.NET Core record DTOs need validation attributes (`[Required]`, etc.) directly on the primary-constructor parameters, not via `[property: Required]` — the latter throws `InvalidOperationException` at request time on record types.
- Backend `Application.Tests` use the EF Core **InMemory** provider (`TestDbContext` in `TestDoubles/`) with fakes for `IClock`/`IEmailSender`/`ITokenService`/`IPasswordHasher` — follow this pattern (not mocking frameworks) for new Application-layer tests.
