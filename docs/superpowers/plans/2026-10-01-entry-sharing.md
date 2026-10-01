# Entry Sharing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user share a single password entry with another PassManager account, recreating that entry's folder path (not just dropping it in the target's root), mirroring the existing folder-sharing feature.

**Architecture:** A new `EntrySharingService` (backend, mirrors `FolderSharingService`) walks the entry's ancestor folder chain via a new `FolderTree.CollectAncestorChain` helper, recreates that chain under the target user's root with fresh IDs (suffixing only the first recreated folder on name collision), copies the entry into the deepest recreated folder, and logs an `EntryShare` audit row. A new `POST /api/entries/{id}/share` endpoint exposes it. The client's `ShareViewModel`/`SharePage` are generalized to drive either a folder-share or entry-share, and `VaultTreePage` gets a "⋯" menu per entry (Modifier/Partager/Supprimer), with the entry detail page's standalone delete button removed.

**Tech Stack:** ASP.NET Core / EF Core / PostgreSQL (backend), .NET MAUI + CommunityToolkit.Mvvm (client), xUnit + EF Core InMemory (Application.Tests).

**Spec:** `docs/superpowers/specs/2026-10-01-entry-sharing-design.md`

## Global Constraints

- Every share call (now folder AND entry) always creates a brand-new copy; never merge with folders/entries created by an earlier share. This is an explicit, confirmed design choice — do not "fix" it later.
- Entries can never be shared as the vault root — there is no `CannotShareRoot` equivalent for entries (only folders have that restriction).
- No new client-side cryptography — the server already decrypts entry passwords at rest (`Vault__MasterKey`), so sharing is a plain server-side copy, same as folder sharing.
- New EF Core migration only — never hand-edit the existing `InitialCreate` migration. Use:
  `dotnet ef migrations add <Name> --project backend/src/Infrastructure/PassManager.Infrastructure.csproj --startup-project backend/src/Api/PassManager.Api.csproj --output-dir Persistence/Migrations`
- Backend tests use the EF Core InMemory provider (`TestDbContext`) with `FakeClock`/other fakes from `backend/tests/Application.Tests/TestDoubles/` — never a mocking framework.
- Controllers translate domain error enums to `ErrorResponseDto("CODE", "message")` with a specific HTTP status per case — never throw raw exceptions for expected failures.

## Review Focus

- Sharing an entry that lives directly in the source user's root folder (no intermediate folders) must copy straight into the target's root with **zero** folders created — an off-by-one in the ancestor walk could either skip the entry or create a spurious extra folder.
- In a multi-level chain (e.g. two ancestor folders), a name collision must suffix **only the first (outermost)** recreated folder — deeper folders in the same chain must never be suffixed even if they also happen to collide by name with something in the target's tree.
- Sharing to a target user ID that doesn't exist must return `TARGET_USER_NOT_FOUND` (404), not a generic 400 — easy to collapse into one catch-all error path.
- Sharing an entry that has already been soft-deleted (`DeletedAt != null`) must be treated as `EntryNotFound`, never silently copied.
- Sharing the **same** entry to the **same** target twice must produce two independent folder chains and two independent entry copies — a future reader may mistake this for a bug and "fix" it into a merge, which would contradict the confirmed design.

---

### Task 1: `EntryShare` domain entity, EF configuration, and migration

**Files:**
- Create: `backend/src/Domain/Entities/EntryShare.cs`
- Create: `backend/src/Infrastructure/Persistence/Configurations/EntryShareConfiguration.cs`
- Modify: `backend/src/Application/Common/IPassManagerDbContext.cs`
- Modify: `backend/src/Infrastructure/Persistence/PassManagerDbContext.cs`
- Modify: `backend/tests/Application.Tests/TestDoubles/TestDbContext.cs`
- Create (generated): `backend/src/Infrastructure/Persistence/Migrations/<timestamp>_AddEntryShare.cs` (+ `.Designer.cs`), modify `PassManagerDbContextModelSnapshot.cs`

**Interfaces:**
- Produces: `EntryShare` class with `Id`, `SourceEntryId`, `SourceOwnerId`, `TargetOwnerId`, `SharedAt` (all required by Task 3).
- Produces: `IPassManagerDbContext.EntryShares` / `PassManagerDbContext.EntryShares` / `TestDbContext.EntryShares` (`DbSet<EntryShare>`), consumed by Task 3's `EntrySharingService`.

- [ ] **Step 1: Create the `EntryShare` entity**

```csharp
namespace PassManager.Domain.Entities;

public class EntryShare
{
    public Guid Id { get; set; }
    public Guid SourceEntryId { get; set; }
    public Guid SourceOwnerId { get; set; }
    public Guid TargetOwnerId { get; set; }
    public DateTimeOffset SharedAt { get; set; }
}
```

- [ ] **Step 2: Create the EF configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassManager.Domain.Entities;

namespace PassManager.Infrastructure.Persistence.Configurations;

public class EntryShareConfiguration : IEntityTypeConfiguration<EntryShare>
{
    public void Configure(EntityTypeBuilder<EntryShare> builder)
    {
        builder.ToTable("entry_shares");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SharedAt).IsRequired();

        builder.HasIndex(s => s.TargetOwnerId);
        builder.HasIndex(s => s.SourceOwnerId);
    }
}
```

- [ ] **Step 3: Wire the new `DbSet` into `IPassManagerDbContext`**

In `backend/src/Application/Common/IPassManagerDbContext.cs`, add this line directly below the existing `DbSet<FolderShare> FolderShares { get; }`:

```csharp
    DbSet<EntryShare> EntryShares { get; }
```

- [ ] **Step 4: Wire the new `DbSet` and configuration into `PassManagerDbContext`**

In `backend/src/Infrastructure/Persistence/PassManagerDbContext.cs`, add below the existing `FolderShares` property:

```csharp
    public DbSet<EntryShare> EntryShares => Set<EntryShare>();
```

And inside `OnModelCreating`, below the existing `modelBuilder.ApplyConfiguration(new FolderShareConfiguration());`:

```csharp
        modelBuilder.ApplyConfiguration(new EntryShareConfiguration());
```

- [ ] **Step 5: Wire the new `DbSet` into `TestDbContext`**

In `backend/tests/Application.Tests/TestDoubles/TestDbContext.cs`, add below the existing `FolderShares` property:

```csharp
    public DbSet<EntryShare> EntryShares => Set<EntryShare>();
```

- [ ] **Step 6: Build to confirm the new types compile**

Run: `dotnet build backend/src/Infrastructure/PassManager.Infrastructure.csproj`
Expected: Build succeeds, 0 errors.

- [ ] **Step 7: Generate the EF Core migration**

Run:
```bash
dotnet ef migrations add AddEntryShare \
  --project backend/src/Infrastructure/PassManager.Infrastructure.csproj \
  --startup-project backend/src/Api/PassManager.Api.csproj \
  --output-dir Persistence/Migrations
```
Expected: a new migration file pair (`<timestamp>_AddEntryShare.cs`, `.Designer.cs`) is created, and `PassManagerDbContextModelSnapshot.cs` is updated to include the `entry_shares` table. Inspect the generated migration's `Up()` method and confirm it creates table `entry_shares` with columns `Id` (PK), `SourceEntryId`, `SourceOwnerId`, `TargetOwnerId`, `SharedAt`, plus indexes on `SourceOwnerId` and `TargetOwnerId`.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Domain/Entities/EntryShare.cs \
        backend/src/Infrastructure/Persistence/Configurations/EntryShareConfiguration.cs \
        backend/src/Application/Common/IPassManagerDbContext.cs \
        backend/src/Infrastructure/Persistence/PassManagerDbContext.cs \
        backend/tests/Application.Tests/TestDoubles/TestDbContext.cs \
        backend/src/Infrastructure/Persistence/Migrations/
git commit -m "feat: add EntryShare entity, EF configuration, and migration"
```

---

### Task 2: `FolderTree.CollectAncestorChain` helper

**Files:**
- Modify: `backend/src/Application/Common/FolderTree.cs`
- Create: `backend/tests/Application.Tests/Common/FolderTreeTests.cs`

**Interfaces:**
- Consumes: `Folder` (`Id`, `ParentId`, `IsRoot`) — existing entity, unchanged.
- Produces: `FolderTree.CollectAncestorChain(IEnumerable<Folder> candidateFolders, Guid folderId) -> List<Folder>`, consumed by Task 3's `EntrySharingService`. Returns the ancestor chain from just-below-root down to (and including) the folder identified by `folderId`, in top-to-bottom order, excluding the true root. Empty list if `folderId` IS the root folder's own ID.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Application.Tests/Common/FolderTreeTests.cs`:

```csharp
using PassManager.Application.Common;
using PassManager.Domain.Entities;
using Xunit;

namespace PassManager.Application.Tests.Common;

public class FolderTreeTests
{
    [Fact]
    public void CollectAncestorChain_FolderIsRoot_ReturnsEmptyChain()
    {
        var rootId = Guid.NewGuid();
        var folders = new List<Folder>
        {
            new() { Id = rootId, ParentId = null, IsRoot = true }
        };

        var chain = FolderTree.CollectAncestorChain(folders, rootId);

        Assert.Empty(chain);
    }

    [Fact]
    public void CollectAncestorChain_NestedTwoLevelsDeep_ReturnsChainInTopToBottomOrder()
    {
        var rootId = Guid.NewGuid();
        var devId = Guid.NewGuid();
        var outilsId = Guid.NewGuid();
        var folders = new List<Folder>
        {
            new() { Id = rootId, ParentId = null, IsRoot = true },
            new() { Id = devId, ParentId = rootId, IsRoot = false, Name = "Dev" },
            new() { Id = outilsId, ParentId = devId, IsRoot = false, Name = "Outils" }
        };

        var chain = FolderTree.CollectAncestorChain(folders, outilsId);

        Assert.Equal(2, chain.Count);
        Assert.Equal("Dev", chain[0].Name);
        Assert.Equal("Outils", chain[1].Name);
    }

    [Fact]
    public void CollectAncestorChain_DirectChildOfRoot_ReturnsSingleElementChain()
    {
        var rootId = Guid.NewGuid();
        var devId = Guid.NewGuid();
        var folders = new List<Folder>
        {
            new() { Id = rootId, ParentId = null, IsRoot = true },
            new() { Id = devId, ParentId = rootId, IsRoot = false, Name = "Dev" }
        };

        var chain = FolderTree.CollectAncestorChain(folders, devId);

        var only = Assert.Single(chain);
        Assert.Equal("Dev", only.Name);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/Application.Tests/PassManager.Application.Tests.csproj --filter "FullyQualifiedName~FolderTreeTests"`
Expected: FAIL to compile — `CollectAncestorChain` does not exist yet.

- [ ] **Step 3: Implement `CollectAncestorChain`**

In `backend/src/Application/Common/FolderTree.cs`, add this method to the existing `FolderTree` static class, below `CollectDescendantIds`:

```csharp
    /// <summary>
    /// Returns the ancestor chain from just-below-root down to (and including) the folder with id
    /// <paramref name="folderId"/>, in top-to-bottom order. Excludes the true root folder. Empty if
    /// <paramref name="folderId"/> IS the root folder's own id.
    /// </summary>
    public static List<Folder> CollectAncestorChain(IEnumerable<Folder> candidateFolders, Guid folderId)
    {
        var byId = candidateFolders.ToDictionary(f => f.Id);
        var chain = new List<Folder>();
        var current = byId[folderId];

        while (!current.IsRoot)
        {
            chain.Add(current);
            if (current.ParentId is null)
            {
                break;
            }

            current = byId[current.ParentId.Value];
        }

        chain.Reverse();
        return chain;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/tests/Application.Tests/PassManager.Application.Tests.csproj --filter "FullyQualifiedName~FolderTreeTests"`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Application/Common/FolderTree.cs backend/tests/Application.Tests/Common/FolderTreeTests.cs
git commit -m "feat: add FolderTree.CollectAncestorChain helper"
```

---

### Task 3: `EntrySharingService`

**Files:**
- Modify: `backend/src/Application/Sharing/SharingModels.cs`
- Create: `backend/src/Application/Sharing/EntrySharingService.cs`
- Create: `backend/tests/Application.Tests/Sharing/EntrySharingServiceTests.cs`

**Interfaces:**
- Consumes: `IPassManagerDbContext` (`Entries`, `Folders`, `Users`, `EntryShares` from Task 1), `IClock`, `FolderTree.CollectAncestorChain` (Task 2).
- Produces: `EntryShareError` enum (`None`, `EntryNotFound`, `TargetUserNotFound`, `CannotShareToSelf`), `EntryShareResult(bool Succeeded, EntryShareError Error, Guid? NewEntryId)`, `EntrySharingService.ShareAsync(Guid requestingUserId, Guid sourceEntryId, Guid targetUserId, CancellationToken ct = default) -> Task<EntryShareResult>` — consumed by Task 4's controller.

- [ ] **Step 1: Write the failing tests**

Create `backend/tests/Application.Tests/Sharing/EntrySharingServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PassManager.Application.Entries;
using PassManager.Application.Folders;
using PassManager.Application.Sharing;
using PassManager.Application.Tests.TestDoubles;
using PassManager.Domain.Entities;
using Xunit;

namespace PassManager.Application.Tests.Sharing;

public class EntrySharingServiceTests
{
    private static async Task<Guid> CreateUserWithRootAsync(TestDbContext db, string email)
    {
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Email = email,
            PasswordHash = "x",
            VaultSalt = [1, 2, 3],
            EmailConfirmed = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Folders.Add(new Folder
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            Name = Folder.RootName,
            IsRoot = true,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return userId;
    }

    private static Task<Guid> GetRootIdAsync(TestDbContext db, Guid ownerId) =>
        db.Folders.Where(f => f.OwnerId == ownerId && f.IsRoot).Select(f => f.Id).SingleAsync();

    [Fact]
    public async Task ShareAsync_EntryTwoLevelsDeep_RecreatesChainAndCopiesEntry()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);

        var folderService = new FolderService(db, clock);
        var entryService = new EntryService(db, clock);
        var dev = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), aliceRoot, "Dev")).Folder!;
        var outils = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), dev.Id, "Outils")).Folder!;
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), outils.Id, "GitHub", "https://github.com", "alice", "secret", "memo")).Entry!;

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);

        Assert.True(result.Succeeded);

        var bobFolders = await folderService.GetFoldersAsync(bob);
        Assert.Equal(3, bobFolders.Count); // root + copied Dev + copied Outils
        var copiedDev = bobFolders.Single(f => f.Name == "Dev");
        var bobRoot = await GetRootIdAsync(db, bob);
        Assert.Equal(bobRoot, copiedDev.ParentId);

        var copiedOutils = bobFolders.Single(f => f.Name == "Outils");
        Assert.Equal(copiedDev.Id, copiedOutils.ParentId);

        var bobEntries = await entryService.GetEntriesAsync(bob, copiedOutils.Id);
        var copiedEntry = Assert.Single(bobEntries);
        Assert.Equal("secret", copiedEntry.Password);
        Assert.Equal(result.NewEntryId, copiedEntry.Id);

        // original untouched
        var aliceEntries = await entryService.GetEntriesAsync(alice, outils.Id);
        Assert.Single(aliceEntries);
    }

    [Fact]
    public async Task ShareAsync_EntryDirectlyInRoot_CopiesToTargetRootWithNoFolderCreated()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var bobRoot = await GetRootIdAsync(db, bob);

        var entryService = new EntryService(db, clock);
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), aliceRoot, "GitHub", null, "alice", "secret", null)).Entry!;

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);

        Assert.True(result.Succeeded);

        var folderService = new FolderService(db, clock);
        var bobFolders = await folderService.GetFoldersAsync(bob);
        Assert.Single(bobFolders); // root only, no folder created

        var bobEntries = await entryService.GetEntriesAsync(bob, bobRoot);
        var copiedEntry = Assert.Single(bobEntries);
        Assert.Equal("GitHub", copiedEntry.Title);
    }

    [Fact]
    public async Task ShareAsync_NameCollisionOnFirstAncestor_OnlySuffixesFirstFolder()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var bobRoot = await GetRootIdAsync(db, bob);

        var folderService = new FolderService(db, clock);
        var entryService = new EntryService(db, clock);
        var dev = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), aliceRoot, "Dev")).Folder!;
        var outils = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), dev.Id, "Outils")).Folder!;
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), outils.Id, "GitHub", null, "alice", "secret", null)).Entry!;

        // Bob already has top-level folders named "Dev" and "Outils" (collision at both levels)
        var bobDev = (await folderService.CreateFolderAsync(bob, Guid.NewGuid(), bobRoot, "Dev")).Folder!;
        await folderService.CreateFolderAsync(bob, Guid.NewGuid(), bobDev.Id, "Outils");

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);

        Assert.True(result.Succeeded);
        var bobFolders = await folderService.GetFoldersAsync(bob);
        Assert.Contains(bobFolders, f => f.Name == "Dev (partagé par alice@example.com)");
        // the deeper "Outils" is a sibling duplicate of Bob's own "Outils" under the ORIGINAL "Dev",
        // but under the NEWLY created "Dev (partagé par ...)" it must keep the plain name "Outils"
        var newDev = bobFolders.Single(f => f.Name == "Dev (partagé par alice@example.com)");
        var outilsUnderNewDev = bobFolders.Single(f => f.ParentId == newDev.Id);
        Assert.Equal("Outils", outilsUnderNewDev.Name);
    }

    [Fact]
    public async Task ShareAsync_SameEntrySharedTwice_CreatesTwoIndependentCopies()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);

        var folderService = new FolderService(db, clock);
        var entryService = new EntryService(db, clock);
        var dev = (await folderService.CreateFolderAsync(alice, Guid.NewGuid(), aliceRoot, "Dev")).Folder!;
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), dev.Id, "GitHub", null, "alice", "secret", null)).Entry!;

        var sharingService = new EntrySharingService(db, clock);
        var first = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);
        var second = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.NewEntryId, second.NewEntryId);

        var bobFolders = await folderService.GetFoldersAsync(bob);
        Assert.Equal(2, bobFolders.Count(f => f.Name == "Dev")); // two independent "Dev" copies, no merge
    }

    [Fact]
    public async Task ShareAsync_DeletedEntry_IsTreatedAsNotFound()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var bob = await CreateUserWithRootAsync(db, "bob@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);

        var entryService = new EntryService(db, clock);
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), aliceRoot, "GitHub", null, "alice", "secret", null)).Entry!;
        await entryService.DeleteEntryAsync(alice, entry.Id);

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, bob, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(EntryShareError.EntryNotFound, result.Error);
    }

    [Fact]
    public async Task ShareAsync_ToSelf_IsRejected()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var entryService = new EntryService(db, clock);
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), aliceRoot, "GitHub", null, "alice", "secret", null)).Entry!;

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, alice, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(EntryShareError.CannotShareToSelf, result.Error);
    }

    [Fact]
    public async Task ShareAsync_ToUnknownTarget_IsRejected()
    {
        var db = TestDbContext.Create();
        var clock = new FakeClock();
        var alice = await CreateUserWithRootAsync(db, "alice@example.com");
        var aliceRoot = await GetRootIdAsync(db, alice);
        var entryService = new EntryService(db, clock);
        var entry = (await entryService.CreateEntryAsync(alice, Guid.NewGuid(), aliceRoot, "GitHub", null, "alice", "secret", null)).Entry!;

        var sharingService = new EntrySharingService(db, clock);
        var result = await sharingService.ShareAsync(alice, entry.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(EntryShareError.TargetUserNotFound, result.Error);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/Application.Tests/PassManager.Application.Tests.csproj --filter "FullyQualifiedName~EntrySharingServiceTests"`
Expected: FAIL to compile — `EntrySharingService`, `EntryShareError`, `EntryShareResult` don't exist yet.

- [ ] **Step 3: Add the error enum and result record**

In `backend/src/Application/Sharing/SharingModels.cs`, add below the existing `UserSummary` record:

```csharp
public enum EntryShareError
{
    None,
    EntryNotFound,
    TargetUserNotFound,
    CannotShareToSelf
}

public record EntryShareResult(bool Succeeded, EntryShareError Error, Guid? NewEntryId);
```

- [ ] **Step 4: Implement `EntrySharingService`**

Create `backend/src/Application/Sharing/EntrySharingService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using PassManager.Application.Common;
using PassManager.Domain.Abstractions;
using PassManager.Domain.Entities;

namespace PassManager.Application.Sharing;

public class EntrySharingService(IPassManagerDbContext db, IClock clock)
{
    public async Task<EntryShareResult> ShareAsync(
        Guid requestingUserId, Guid sourceEntryId, Guid targetUserId, CancellationToken ct = default)
    {
        if (requestingUserId == targetUserId)
        {
            return new EntryShareResult(false, EntryShareError.CannotShareToSelf, null);
        }

        var sourceEntry = await db.Entries
            .FirstOrDefaultAsync(e => e.Id == sourceEntryId && e.OwnerId == requestingUserId && e.DeletedAt == null, ct);
        if (sourceEntry is null)
        {
            return new EntryShareResult(false, EntryShareError.EntryNotFound, null);
        }

        var targetRoot = await db.Folders
            .FirstOrDefaultAsync(f => f.OwnerId == targetUserId && f.IsRoot && f.DeletedAt == null, ct);
        if (targetRoot is null)
        {
            return new EntryShareResult(false, EntryShareError.TargetUserNotFound, null);
        }

        var sourceOwnerFolders = await db.Folders
            .Where(f => f.OwnerId == requestingUserId && f.DeletedAt == null)
            .ToListAsync(ct);
        var ancestorChain = FolderTree.CollectAncestorChain(sourceOwnerFolders, sourceEntry.FolderId);

        var requestingUser = await db.Users.FirstAsync(u => u.Id == requestingUserId, ct);
        var now = clock.UtcNow;
        var destinationFolderId = targetRoot.Id;

        if (ancestorChain.Count > 0)
        {
            var existingTopLevelNames = await db.Folders
                .Where(f => f.OwnerId == targetUserId && f.ParentId == targetRoot.Id && f.DeletedAt == null)
                .Select(f => f.Name)
                .ToListAsync(ct);

            var topFolderName = existingTopLevelNames.Contains(ancestorChain[0].Name)
                ? $"{ancestorChain[0].Name} (partagé par {requestingUser.Email})"
                : ancestorChain[0].Name;

            var parentId = targetRoot.Id;
            for (var i = 0; i < ancestorChain.Count; i++)
            {
                var newFolder = new Folder
                {
                    Id = Guid.NewGuid(),
                    OwnerId = targetUserId,
                    ParentId = parentId,
                    Name = i == 0 ? topFolderName : ancestorChain[i].Name,
                    IsRoot = false,
                    Version = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Folders.Add(newFolder);
                parentId = newFolder.Id;
            }

            destinationFolderId = parentId;
        }

        var newEntryId = Guid.NewGuid();
        db.Entries.Add(new Entry
        {
            Id = newEntryId,
            FolderId = destinationFolderId,
            OwnerId = targetUserId,
            Title = sourceEntry.Title,
            Url = sourceEntry.Url,
            Login = sourceEntry.Login,
            Password = sourceEntry.Password,
            Memo = sourceEntry.Memo,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        });

        db.EntryShares.Add(new EntryShare
        {
            Id = Guid.NewGuid(),
            SourceEntryId = sourceEntryId,
            SourceOwnerId = requestingUserId,
            TargetOwnerId = targetUserId,
            SharedAt = now
        });

        await db.SaveChangesAsync(ct);

        return new EntryShareResult(true, EntryShareError.None, newEntryId);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/tests/Application.Tests/PassManager.Application.Tests.csproj --filter "FullyQualifiedName~EntrySharingServiceTests"`
Expected: PASS, 7 tests.

- [ ] **Step 6: Run the full Application.Tests suite to confirm no regressions**

Run: `dotnet test backend/tests/Application.Tests/PassManager.Application.Tests.csproj`
Expected: PASS, all tests (existing + new).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Application/Sharing/SharingModels.cs \
        backend/src/Application/Sharing/EntrySharingService.cs \
        backend/tests/Application.Tests/Sharing/EntrySharingServiceTests.cs
git commit -m "feat: add EntrySharingService with ancestor-chain folder recreation"
```

---

### Task 4: API endpoint for entry sharing

**Files:**
- Modify: `backend/src/Api/Dtos/SharingDtos.cs`
- Modify: `backend/src/Api/Controllers/EntriesController.cs`
- Modify: `backend/src/Api/Program.cs`

**Interfaces:**
- Consumes: `EntrySharingService.ShareAsync` (Task 3), `EntryShareError` (Task 3).
- Produces: `POST /api/entries/{id}/share` (body: `{ "targetUserId": "<guid>" }`, 200 response: `{ "newEntryId": "<guid>" }`), consumed by Task 5's `HttpShareApiClient`.

- [ ] **Step 1: Add the request/response DTOs**

In `backend/src/Api/Dtos/SharingDtos.cs`, add below the existing `UserSummaryDto`:

```csharp
public record ShareEntryRequestDto([Required] Guid TargetUserId);

public record ShareEntryResponseDto(Guid NewEntryId);
```

- [ ] **Step 2: Add the share action to `EntriesController`**

In `backend/src/Api/Controllers/EntriesController.cs`:

1. Add `using PassManager.Application.Sharing;` to the usings.
2. Change the primary constructor from `EntriesController(EntryService entryService)` to:
   ```csharp
   public class EntriesController(EntryService entryService, EntrySharingService sharingService) : ControllerBase
   ```
3. Add this action (place it after `Delete`, before the private `ToDto` helper):

```csharp
    [HttpPost("{id:guid}/share")]
    public async Task<IActionResult> Share(Guid id, ShareEntryRequestDto request, CancellationToken ct)
    {
        var result = await sharingService.ShareAsync(this.GetUserId(), id, request.TargetUserId, ct);
        if (!result.Succeeded)
        {
            return result.Error switch
            {
                EntryShareError.EntryNotFound => NotFound(new ErrorResponseDto("ENTRY_NOT_FOUND", "Entrée introuvable.")),
                EntryShareError.TargetUserNotFound => NotFound(new ErrorResponseDto("TARGET_USER_NOT_FOUND", "Compte destinataire introuvable.")),
                EntryShareError.CannotShareToSelf => BadRequest(new ErrorResponseDto("CANNOT_SHARE_TO_SELF", "Impossible de partager une entrée avec son propre compte.")),
                _ => BadRequest(new ErrorResponseDto("INVALID_REQUEST", "Impossible de partager cette entrée."))
            };
        }

        return Ok(new ShareEntryResponseDto(result.NewEntryId!.Value));
    }
```

- [ ] **Step 3: Register `EntrySharingService` in `Program.cs`**

In `backend/src/Api/Program.cs`, add directly below the existing `builder.Services.AddScoped<FolderSharingService>();`:

```csharp
builder.Services.AddScoped<EntrySharingService>();
```

- [ ] **Step 4: Build the backend to confirm it compiles**

Run: `dotnet build backend/src/Api/PassManager.Api.csproj`
Expected: Build succeeds, 0 errors.

- [ ] **Step 5: Manually verify the endpoint against the running API**

```bash
docker compose --env-file infra/.env -f infra/docker-compose.yml up --build -d
```

Register/confirm/login two test accounts (or reuse existing ones), create a nested entry for account A, then:

```bash
curl -s -X POST http://localhost:8080/api/entries/<entryId>/share \
  -H "Authorization: Bearer <A's access token>" \
  -H "Content-Type: application/json" \
  -d '{"targetUserId":"<B's user id>"}'
```

Expected: `200 OK` with `{"newEntryId":"<guid>"}`. Repeat with a bogus `targetUserId` and confirm `404` with `TARGET_USER_NOT_FOUND`.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Api/Dtos/SharingDtos.cs backend/src/Api/Controllers/EntriesController.cs backend/src/Api/Program.cs
git commit -m "feat: expose POST /api/entries/{id}/share endpoint"
```

---

### Task 5: Client API — `ShareEntryAsync`

**Files:**
- Modify: `src/PassManager.Core/Sharing/IShareApiClient.cs`
- Modify: `src/PassManager.Maui/Services/Platform/HttpShareApiClient.cs`

**Interfaces:**
- Produces: `IShareApiClient.ShareEntryAsync(Guid entryId, Guid targetUserId, CancellationToken ct = default) -> Task<Guid>`, consumed by Task 6's `ShareViewModel`.

- [ ] **Step 1: Extend the interface**

In `src/PassManager.Core/Sharing/IShareApiClient.cs`, add below the existing `ShareFolderAsync`:

```csharp
    Task<Guid> ShareEntryAsync(Guid entryId, Guid targetUserId, CancellationToken ct = default);
```

- [ ] **Step 2: Implement it in `HttpShareApiClient`**

In `src/PassManager.Maui/Services/Platform/HttpShareApiClient.cs`, add below the existing `ShareFolderAsync` method (and above the existing `ShareResponsePayload` record):

```csharp
    public async Task<Guid> ShareEntryAsync(Guid entryId, Guid targetUserId, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync($"api/entries/{entryId}/share", new { targetUserId }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ShareEntryResponsePayload>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Réponse de partage invalide.");
        return payload.NewEntryId;
    }
```

And add this record next to the existing `ShareResponsePayload` record at the bottom of the file:

```csharp
    private record ShareEntryResponsePayload(Guid NewEntryId);
```

- [ ] **Step 3: Build the client to confirm it compiles**

Run: `dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0`
Expected: Build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/PassManager.Core/Sharing/IShareApiClient.cs src/PassManager.Maui/Services/Platform/HttpShareApiClient.cs
git commit -m "feat: add ShareEntryAsync to the share API client"
```

---

### Task 6: Generalize `ShareViewModel`/`SharePage` for entry shares

**Files:**
- Modify: `src/PassManager.Maui/ViewModels/ShareViewModel.cs`
- Modify: `src/PassManager.Maui/Views/SharePage.xaml`

**Interfaces:**
- Consumes: `IShareApiClient.ShareEntryAsync`/`ShareFolderAsync` (Task 5).
- Produces: `ShareViewModel` now accepts Shell query parameters `EntryId`/`EntryName` in addition to the existing `FolderId`/`FolderName`; its public `FolderName` property is renamed to `ItemName` (consumed by Task 7's navigation call and by `SharePage.xaml`'s binding).

- [ ] **Step 1: Rewrite `ShareViewModel`**

Replace the full contents of `src/PassManager.Maui/ViewModels/ShareViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PassManager.Core.Sharing;

namespace PassManager.Maui.ViewModels;

public partial class ShareViewModel(IShareApiClient shareApiClient) : ObservableObject, IQueryAttributable
{
    private Guid _itemId;
    private bool _isEntry;

    [ObservableProperty]
    private string itemName = "";

    [ObservableProperty]
    private string query = "";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorMessage;

    public ObservableCollection<UserSummary> Results { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("FolderId", out var folderId) && folderId is Guid folderGuid)
        {
            _itemId = folderGuid;
            _isEntry = false;
        }
        else if (query.TryGetValue("EntryId", out var entryId) && entryId is Guid entryGuid)
        {
            _itemId = entryGuid;
            _isEntry = true;
        }

        if (query.TryGetValue("FolderName", out var folderName) && folderName is string folderNameValue)
        {
            ItemName = folderNameValue;
        }
        else if (query.TryGetValue("EntryName", out var entryName) && entryName is string entryNameValue)
        {
            ItemName = entryNameValue;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;
        try
        {
            Results.Clear();
            foreach (var user in await shareApiClient.SearchUsersAsync(Query.Trim()))
            {
                Results.Add(user);
            }

            if (Results.Count == 0)
            {
                StatusMessage = "Aucun compte trouvé (2 caractères minimum).";
            }
        }
        catch (Exception)
        {
            ErrorMessage = "Recherche impossible.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ShareWithAsync(UserSummary user)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;
        try
        {
            if (_isEntry)
            {
                await shareApiClient.ShareEntryAsync(_itemId, user.Id);
            }
            else
            {
                await shareApiClient.ShareFolderAsync(_itemId, user.Id);
            }

            StatusMessage = $"« {ItemName} » a été partagé avec {user.Email}. Le destinataire le verra à sa prochaine synchronisation.";
        }
        catch (Exception)
        {
            ErrorMessage = "Le partage a échoué.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
```

- [ ] **Step 2: Update the binding in `SharePage.xaml`**

In `src/PassManager.Maui/Views/SharePage.xaml`, find:

```xml
                <Label Text="{Binding FolderName, StringFormat='Partager « {0} »'}" Style="{StaticResource TitleLabel}" />
```

Replace with:

```xml
                <Label Text="{Binding ItemName, StringFormat='Partager « {0} »'}" Style="{StaticResource TitleLabel}" />
```

- [ ] **Step 3: Build the client to confirm it compiles**

Run: `dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0`
Expected: Build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/PassManager.Maui/ViewModels/ShareViewModel.cs src/PassManager.Maui/Views/SharePage.xaml
git commit -m "feat: generalize ShareViewModel to drive folder or entry shares"
```

---

### Task 7: Entry "⋯" menu in `VaultTreePage` (Modifier/Partager/Supprimer)

**Files:**
- Modify: `src/PassManager.Maui/ViewModels/VaultTreeViewModel.cs`
- Modify: `src/PassManager.Maui/Views/VaultTreePage.xaml`

**Interfaces:**
- Consumes: `VaultRepository.DeleteEntry(Guid)` (existing, `src/PassManager.Core/Vault/VaultRepository.cs`), `AuthSessionService.SaveVaultAsync` (existing), `INavigationService.NavigateToAsync` (existing).
- Produces: `VaultTreeViewModel.EntryOptionsCommand` (`IRelayCommand<EntryRowViewModel>`, generated by `[RelayCommand]` from a method named `EntryOptionsAsync`), bound from `VaultTreePage.xaml`.

- [ ] **Step 1: Add the `EntryOptionsAsync` command**

In `src/PassManager.Maui/ViewModels/VaultTreeViewModel.cs`, add this method directly below the existing `FolderOptionsAsync` method:

```csharp
    [RelayCommand]
    private async Task EntryOptionsAsync(EntryRowViewModel entry)
    {
        var vault = authSession.Vault;
        if (vault is null)
        {
            return;
        }

        var page = Shell.Current.CurrentPage;
        var action = await page.DisplayActionSheet(entry.Title, "Annuler", null, "Modifier", "Partager", "Supprimer");

        switch (action)
        {
            case "Modifier":
                await navigation.NavigateToAsync(nameof(EntryDetailPage), new Dictionary<string, object> { ["EntryId"] = entry.Id });
                break;

            case "Partager":
                await navigation.NavigateToAsync(nameof(SharePage), new Dictionary<string, object> { ["EntryId"] = entry.Id, ["EntryName"] = entry.Title });
                break;

            case "Supprimer":
                var confirmed = await page.DisplayAlert("Confirmer", $"Supprimer « {entry.Title} » ?", "Supprimer", "Annuler");
                if (confirmed)
                {
                    vault.DeleteEntry(entry.Id);
                    await authSession.SaveVaultAsync();
                    RefreshEntries();
                }
                break;
        }
    }
```

- [ ] **Step 2: Add the "⋯" button to the entry row template**

In `src/PassManager.Maui/Views/VaultTreePage.xaml`, find the entries `DataTemplate`:

```xml
                        <DataTemplate x:DataType="vm:EntryRowViewModel">
                            <Grid Padding="20,12" ColumnDefinitions="*,*">
                                <Grid.GestureRecognizers>
                                    <TapGestureRecognizer
                                        Command="{Binding BindingContext.OpenEntryCommand, Source={x:Reference RootPage}}"
                                        CommandParameter="{Binding .}" />
                                </Grid.GestureRecognizers>
                                <Label Grid.Column="0" Text="{Binding Title}" FontFamily="OpenSansSemibold" />
                                <Label Grid.Column="1" Text="{Binding Login}" TextColor="{StaticResource Gray500}" HorizontalOptions="End" />
                            </Grid>
                        </DataTemplate>
```

Replace with:

```xml
                        <DataTemplate x:DataType="vm:EntryRowViewModel">
                            <Grid Padding="20,12" ColumnDefinitions="*,*,Auto">
                                <Grid.GestureRecognizers>
                                    <TapGestureRecognizer
                                        Command="{Binding BindingContext.OpenEntryCommand, Source={x:Reference RootPage}}"
                                        CommandParameter="{Binding .}" />
                                </Grid.GestureRecognizers>
                                <Label Grid.Column="0" Text="{Binding Title}" FontFamily="OpenSansSemibold" />
                                <Label Grid.Column="1" Text="{Binding Login}" TextColor="{StaticResource Gray500}" HorizontalOptions="End" />
                                <Button Grid.Column="2" Text="⋯" WidthRequest="32" MinimumWidthRequest="32" MinimumHeightRequest="32"
                                        Padding="0" TextColor="{StaticResource Gray500}" BackgroundColor="Transparent"
                                        Command="{Binding BindingContext.EntryOptionsCommand, Source={x:Reference RootPage}}"
                                        CommandParameter="{Binding .}" />
                            </Grid>
                        </DataTemplate>
```

- [ ] **Step 3: Build the client to confirm it compiles**

Run: `dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0`
Expected: Build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/PassManager.Maui/ViewModels/VaultTreeViewModel.cs src/PassManager.Maui/Views/VaultTreePage.xaml
git commit -m "feat: add entry options menu (Modifier/Partager/Supprimer) to VaultTreePage"
```

---

### Task 8: Remove the standalone delete button from `EntryDetailPage`

**Files:**
- Modify: `src/PassManager.Maui/Views/EntryDetailPage.xaml`
- Modify: `src/PassManager.Maui/ViewModels/EntryDetailViewModel.cs`

**Interfaces:**
- None — this only removes a command and its UI trigger; `DeleteEntryAsync`/deletion now lives exclusively in Task 7's `VaultTreeViewModel.EntryOptionsAsync`.

- [ ] **Step 1: Remove the delete button and its surrounding divider from the XAML**

In `src/PassManager.Maui/Views/EntryDetailPage.xaml`, remove these two elements (the divider and the delete button), which currently sit between the error label and the end of the `VerticalStackLayout`:

```xml
                    <BoxView HeightRequest="1" Color="{AppThemeBinding Light={StaticResource Gray200}, Dark={StaticResource Gray600}}" Margin="0,8" />

                    <HorizontalStackLayout Spacing="8">
                        <Button Text="Enregistrer" Command="{Binding SaveCommand}" />
                        <Button Text="Annuler" Command="{Binding CancelCommand}" BackgroundColor="Transparent" TextColor="{StaticResource Gray500}" />
                    </HorizontalStackLayout>

                    <Button Text="Supprimer" Command="{Binding DeleteCommand}" TextColor="{StaticResource Danger}" BackgroundColor="Transparent" HorizontalOptions="Start" />
```

Replace with (keeps Enregistrer/Annuler, drops the divider and the Supprimer button):

```xml
                    <HorizontalStackLayout Spacing="8">
                        <Button Text="Enregistrer" Command="{Binding SaveCommand}" />
                        <Button Text="Annuler" Command="{Binding CancelCommand}" BackgroundColor="Transparent" TextColor="{StaticResource Gray500}" />
                    </HorizontalStackLayout>
```

- [ ] **Step 2: Remove the `DeleteAsync` command from the ViewModel**

In `src/PassManager.Maui/ViewModels/EntryDetailViewModel.cs`, remove this method entirely:

```csharp
    [RelayCommand]
    private async Task DeleteAsync()
    {
        var vault = authSession.Vault;
        if (vault is null)
        {
            return;
        }

        var confirmed = await Shell.Current.CurrentPage.DisplayAlert("Confirmer", "Supprimer cette entrée ?", "Supprimer", "Annuler");
        if (!confirmed)
        {
            return;
        }

        vault.DeleteEntry(_entryId);
        await authSession.SaveVaultAsync();
        await navigation.GoBackAsync();
    }
```

- [ ] **Step 3: Build the client to confirm it compiles**

Run: `dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0`
Expected: Build succeeds, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/PassManager.Maui/Views/EntryDetailPage.xaml src/PassManager.Maui/ViewModels/EntryDetailViewModel.cs
git commit -m "refactor: remove standalone delete button from EntryDetailPage"
```

---

### Task 9: End-to-end manual verification

**Files:** none (verification only).

**Interfaces:** none.

- [ ] **Step 1: Rebuild and restart the backend**

```bash
docker compose --env-file infra/.env -f infra/docker-compose.yml up --build -d
```

Expected: `api` and `postgres` containers start cleanly; `Database__AutoMigrate=true` applies the new `AddEntryShare` migration automatically (confirm via `docker compose -f infra/docker-compose.yml logs api | grep -i migrat`).

- [ ] **Step 2: Rebuild and launch the Windows client**

```bash
dotnet build src/PassManager.Maui/PassManager.Maui.csproj -f net10.0-windows10.0.19041.0
```

Launch `src/PassManager.Maui/bin/Debug/net10.0-windows10.0.19041.0/win-x64/PassManager.Maui.exe`.

- [ ] **Step 3: Walk the full entry-share flow with two accounts**

Using two already-confirmed test accounts (or registering a second one):
1. Log in as account A, create a nested folder (e.g. `Dev > Outils`), create an entry inside it.
2. Click the entry's "⋯" menu → "Partager" → search for account B's email → tap "Partager".
3. Confirm the status message `"« <title> » a été partagé avec <B's email>..."` appears.
4. Log out, log in as account B, click "Synchroniser".
5. Confirm the folder chain `Dev > Outils` (or `Dev (partagé par <A's email>) > Outils` if B already had a top-level `Dev`) appears under B's Racine, with the shared entry inside it.
6. Back on account A's entry detail page, confirm the standalone "Supprimer" button is gone, and that "⋯" → "Supprimer" on the entries list still deletes correctly (with confirmation prompt) and "⋯" → "Modifier" still opens the entry detail page.

Expected: all steps behave as described, matching the approved design and spec.
