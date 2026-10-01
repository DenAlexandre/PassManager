using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassManager.Api.Common;
using PassManager.Api.Dtos;
using PassManager.Application.Entries;
using PassManager.Application.Sharing;

namespace PassManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/entries")]
public class EntriesController(EntryService entryService, EntrySharingService sharingService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetByFolder([FromQuery] Guid folderId, CancellationToken ct)
    {
        var entries = await entryService.GetEntriesAsync(this.GetUserId(), folderId, ct);
        return Ok(entries.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateEntryRequestDto request, CancellationToken ct)
    {
        var result = await entryService.CreateEntryAsync(
            this.GetUserId(), request.Id, request.FolderId, request.Title, request.Url, request.Login,
            request.Password, request.Memo, ct);

        if (!result.Succeeded)
        {
            return result.Error switch
            {
                EntryError.FolderNotFound => NotFound(new ErrorResponseDto("FOLDER_NOT_FOUND", "Le dossier cible est introuvable.")),
                EntryError.DuplicateId => Conflict(new ErrorResponseDto("DUPLICATE_ID", "Cet identifiant d'entrée existe déjà.")),
                _ => BadRequest(new ErrorResponseDto("INVALID_REQUEST", "Impossible de créer l'entrée."))
            };
        }

        return CreatedAtAction(nameof(GetByFolder), new { folderId = request.FolderId }, ToDto(result.Entry!));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateEntryRequestDto request, CancellationToken ct)
    {
        var result = await entryService.UpdateEntryAsync(
            this.GetUserId(), id, request.Title, request.Url, request.Login, request.Password, request.Memo, ct);

        if (!result.Succeeded)
        {
            return NotFound(new ErrorResponseDto("ENTRY_NOT_FOUND", "Entrée introuvable."));
        }

        return Ok(ToDto(result.Entry!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await entryService.DeleteEntryAsync(this.GetUserId(), id, ct);
        if (!result.Succeeded)
        {
            return NotFound(new ErrorResponseDto("ENTRY_NOT_FOUND", "Entrée introuvable."));
        }

        return NoContent();
    }

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

    private static EntryDto ToDto(Domain.Entities.Entry entry) =>
        new(entry.Id, entry.FolderId, entry.Title, entry.Url, entry.Login, entry.Password, entry.Memo, entry.Version, entry.UpdatedAt);
}
