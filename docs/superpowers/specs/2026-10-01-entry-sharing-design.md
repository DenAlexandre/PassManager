# Entry sharing — design spec

Date: 2026-10-01

## Context and goal

PassManager already supports sharing an entire folder subtree with another
account (`FolderSharingService`, `POST /api/folders/{id}/share`): it
deep-copies the folder and its descendants/entries into the target
account's root folder, with fresh IDs, and records an audit row.

This spec adds a parallel capability: sharing a **single entry** instead of
a whole folder. The entry's existing folder path (the chain of folder
names from just below the vault root down to the entry's immediate
parent) is recreated on the target account so the entry doesn't simply
land in the target's root — it keeps its original place in a
newly-created copy of that path.

This reuses the server-can-decrypt architecture already in place for
folder sharing (see CLAUDE.md: "No zero-knowledge encryption") — no new
client-side crypto is needed.

## Non-goals

- No merging with folder/entry trees created by earlier shares. Every
  share (folder or entry) always creates a fresh copy, exactly like the
  existing folder-sharing behavior. Repeated entry shares to the same
  account will create duplicate folder chains over time — this is an
  accepted, explicit tradeoff (confirmed during design), not an oversight.
- No bulk/multi-entry sharing in one request — one entry per share call,
  matching the existing one-folder-per-share-call shape.
- No change to folder sharing itself.

## Data flow

Given an entry `E` owned by user `A`, located at
`Racine(A) > Dev > Outils > E`, shared to user `B`:

1. Walk `E.FolderId` upward via `ParentId` until reaching `A`'s root
   folder (`IsRoot == true`), collecting the chain in top-to-bottom
   order: `[Dev, Outils]`. The true root folder itself is excluded from
   the chain (it never gets copied — target's own root is the anchor).
2. If the chain is non-empty: recreate each folder in order under `B`'s
   root, with fresh IDs. The **first** recreated folder (`Dev`) is
   checked against `B`'s existing top-level folder names; on collision
   it gets the suffix `" (partagé par {A.Email})"`, exactly like
   `FolderSharingService` does today. Folders deeper in the chain
   (`Outils`) are never renamed, same as today's subtree copies.
3. If the chain is empty (the entry lives directly in `A`'s root), skip
   step 2 entirely — the entry is copied straight into `B`'s root.
4. Copy `E` (fresh `Id`, same `Title`/`Url`/`Login`/`Password`/`Memo`,
   `Version = 1`) into the last folder created in step 2, or into `B`'s
   root if step 2 was skipped.
5. Record an `EntryShare` audit row (`SourceEntryId`, `SourceOwnerId`,
   `TargetOwnerId`, `SharedAt`) — read-only history, mirrors
   `FolderShare`.

Every call to the share endpoint repeats this whole process from
scratch; there is no lookup for "did I already create this chain" on
the target side.

## Components

### Backend

**`Application/Common/FolderTree.cs`** (existing static helper — extend,
don't replace): add
`CollectAncestorChain(IEnumerable<Folder> candidateFolders, Guid folderId) -> List<Folder>`,
returning the ancestor folders from just-below-root down to (and
including) the folder identified by `folderId`, in top-to-bottom order,
stopping at (and excluding) the folder with `IsRoot == true`. Pure
function, no DB access, same shape as the existing
`CollectDescendantIds`.

**`Application/Sharing/EntrySharingService.cs`** (new, sibling to
`FolderSharingService`):
```csharp
public class EntrySharingService(IPassManagerDbContext db, IClock clock)
{
    public async Task<EntryShareResult> ShareAsync(
        Guid requestingUserId, Guid sourceEntryId, Guid targetUserId, CancellationToken ct = default)
    { ... }
}
```
Logic follows the Data flow section above. Loads the source entry
(`OwnerId == requestingUserId && DeletedAt == null`), the source owner's
full folder list (to walk the ancestor chain), and the target's root
folder — same query shapes `FolderSharingService` already uses. Returns
`NewEntryId` on success.

**`Application/Sharing/SharingModels.cs`** (existing file — extend): add
```csharp
public enum EntryShareError { None, EntryNotFound, TargetUserNotFound, CannotShareToSelf }
public record EntryShareResult(bool Succeeded, EntryShareError Error, Guid? NewEntryId);
```
No `CannotShareRoot` equivalent — an entry is never a root folder.

**`Domain/Entities/EntryShare.cs`** (new): `Id`, `SourceEntryId`,
`SourceOwnerId`, `TargetOwnerId`, `SharedAt` — identical shape to
`FolderShare.cs`.

**`Infrastructure/Persistence/Configurations/EntryShareConfiguration.cs`**
(new): mirrors `FolderShareConfiguration.cs` — table `entry_shares`,
indexes on `SourceOwnerId` and `TargetOwnerId`.

**`Application/Common/IPassManagerDbContext.cs`**: add
`DbSet<EntryShare> EntryShares { get; }`.

**EF Core migration**: new migration (e.g. `AddEntryShare`) adding the
`entry_shares` table — follow the standard workflow in CLAUDE.md
(`dotnet ef migrations add ...`), do not hand-edit the existing
`InitialCreate` migration.

**`Api/Dtos/SharingDtos.cs`** (existing file — extend): add
```csharp
public record ShareEntryRequestDto([Required] Guid TargetUserId);
public record ShareEntryResponseDto(Guid NewEntryId);
```

**`Api/Controllers/EntriesController.cs`** (existing controller — add an
action): `POST /api/entries/{id}/share`, body `ShareEntryRequestDto`,
calls `EntrySharingService.ShareAsync`, maps `EntryShareError` to HTTP
responses the same way `FoldersController.Share` maps `ShareError`:
- `EntryNotFound` → 404 `ENTRY_NOT_FOUND`
- `TargetUserNotFound` → 404 `TARGET_USER_NOT_FOUND`
- `CannotShareToSelf` → 400 `CANNOT_SHARE_TO_SELF`
- success → 200 `ShareEntryResponseDto`

**`Api/Program.cs`**: register `builder.Services.AddScoped<EntrySharingService>();`
next to the existing `FolderSharingService` registration.

User search (`GET /api/users/search`) is reused as-is — no changes
needed there.

### Client (`PassManager.Core` + `PassManager.Maui`)

**`Core/Sharing/IShareApiClient.cs`**: add
`Task<Guid> ShareEntryAsync(Guid entryId, Guid targetUserId, CancellationToken ct = default);`
alongside the existing `ShareFolderAsync`.

**`Maui/Services/Platform/HttpShareApiClient.cs`**: implement the new
method against `POST /api/entries/{id}/share`, same pattern as the
existing folder-share call.

**`Maui/ViewModels/ShareViewModel.cs`**: generalize from
folder-only to folder-or-entry. Replace the single `_folderId` field
and `FolderName` property with a small discriminated shape, e.g.:
```csharp
private Guid _itemId;
private bool _isEntry;
[ObservableProperty] private string itemName = ""; // was FolderName
```
`ApplyQueryAttributes` accepts either `FolderId`+`FolderName` or
`EntryId`+`EntryName` navigation parameters (both navigations pass
whichever one applies). `ShareWithAsync` calls `ShareFolderAsync` or
`ShareEntryAsync` depending on `_isEntry`. `SharePage.xaml` only needs
its title binding source renamed (`FolderName` → `ItemName`) — no
structural change.

**`Maui/Views/VaultTreePage.xaml`** entries list: replace the current
plain two-column entry row with a row that adds a "⋯" `Button`
(same visual pattern as the folder row's existing options button),
wired to a new `EntryOptionsCommand` on `VaultTreeViewModel`. That
command shows an action sheet — "Modifier" (navigates to
`EntryDetailPage`, same as today's tap-to-open), "Supprimer" (calls
`vault.DeleteEntry` + `SaveVaultAsync` + refresh, same pattern as
`FolderOptionsAsync`'s "Supprimer" branch), "Partager" (navigates to
`SharePage` with `EntryId`/`EntryName` query parameters instead of
`FolderId`/`FolderName`).

**`Maui/Views/EntryDetailPage.xaml` / `EntryDetailViewModel.cs`**:
remove the "Supprimer" button and its `DeleteCommand` from the entry
detail page — deletion now only happens from the "⋯" menu in the
entries list (per the approved chat design). Keep "Enregistrer" /
"Annuler" as-is.

## Error handling

Mirrors the existing folder-sharing error handling exactly: domain
errors are modeled as an enum + result record (no exceptions for
expected failure cases), the controller translates each enum value to a
specific HTTP status + `ErrorResponseDto` code, and the client
ViewModel catches `Exception` broadly around the API call and shows a
generic French error message (`"Le partage a échoué."`), consistent
with how `ShareWithAsync` already behaves for folder shares.

## Testing

New `Application.Tests` tests for `EntrySharingService`, following the
existing pattern (EF Core InMemory `TestDbContext`, fake `IClock`):
- Sharing an entry nested two levels deep recreates both ancestor
  folders under the target's root and places the entry copy in the
  deepest one.
- Sharing an entry that lives directly in the source's root copies it
  straight into the target's root with no folder created.
- Name collision on the first recreated folder gets the
  `" (partagé par {email})"` suffix; deeper folders in the same chain
  do not get suffixed even if they also collide by name.
- Sharing the same entry twice to the same target creates two separate
  folder chains and two separate entry copies (confirms the "always
  fresh copy" behavior is intentional, not a bug).
- `EntryNotFound`, `TargetUserNotFound`, `CannotShareToSelf` each
  produce the correct `EntryShareError`.

Also add a unit test for `FolderTree.CollectAncestorChain` directly
(pure function, no DB) covering: entry in root (empty chain), entry
nested N levels deep (chain of length N, correct order).

No new `PassManager.Core.Tests` coverage is needed — `IShareApiClient`
is a thin HTTP wrapper with no client-side logic to unit test, same as
today's `ShareFolderAsync`.
