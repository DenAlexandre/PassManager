using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassManager.Api.Common;
using PassManager.Api.Dtos;
using PassManager.Application.Folders;
using PassManager.Application.Sharing;

namespace PassManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/folders")]
public class FoldersController(FolderService folderService, FolderSharingService sharingService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var folders = await folderService.GetFoldersAsync(this.GetUserId(), ct);
        return Ok(folders.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateFolderRequestDto request, CancellationToken ct)
    {
        var result = await folderService.CreateFolderAsync(this.GetUserId(), request.Id, request.ParentId, request.Name, ct);
        if (!result.Succeeded)
        {
            return result.Error switch
            {
                FolderError.ParentNotFound => NotFound(new ErrorResponseDto("PARENT_NOT_FOUND", "Le dossier parent est introuvable.")),
                FolderError.DuplicateId => Conflict(new ErrorResponseDto("DUPLICATE_ID", "Cet identifiant de dossier existe déjà.")),
                _ => BadRequest(new ErrorResponseDto("INVALID_REQUEST", "Impossible de créer le dossier."))
            };
        }

        return CreatedAtAction(nameof(GetAll), ToDto(result.Folder!));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, RenameFolderRequestDto request, CancellationToken ct)
    {
        var result = await folderService.RenameFolderAsync(this.GetUserId(), id, request.Name, ct);
        if (!result.Succeeded)
        {
            return result.Error switch
            {
                FolderError.CannotModifyRoot => BadRequest(new ErrorResponseDto("CANNOT_MODIFY_ROOT", "Le dossier Racine ne peut pas être renommé.")),
                _ => NotFound(new ErrorResponseDto("FOLDER_NOT_FOUND", "Dossier introuvable."))
            };
        }

        return Ok(ToDto(result.Folder!));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await folderService.DeleteFolderAsync(this.GetUserId(), id, ct);
        if (!result.Succeeded)
        {
            return result.Error switch
            {
                FolderError.CannotModifyRoot => BadRequest(new ErrorResponseDto("CANNOT_MODIFY_ROOT", "Le dossier Racine ne peut pas être supprimé.")),
                _ => NotFound(new ErrorResponseDto("FOLDER_NOT_FOUND", "Dossier introuvable."))
            };
        }

        return NoContent();
    }

    [HttpPost("{id:guid}/share")]
    public async Task<IActionResult> Share(Guid id, ShareFolderRequestDto request, CancellationToken ct)
    {
        var result = await sharingService.ShareAsync(this.GetUserId(), id, request.TargetUserId, ct);
        if (!result.Succeeded)
        {
            return result.Error switch
            {
                ShareError.FolderNotFound => NotFound(new ErrorResponseDto("FOLDER_NOT_FOUND", "Dossier introuvable.")),
                ShareError.CannotShareRoot => BadRequest(new ErrorResponseDto("CANNOT_SHARE_ROOT", "Le dossier Racine ne peut pas être partagé.")),
                ShareError.TargetUserNotFound => NotFound(new ErrorResponseDto("TARGET_USER_NOT_FOUND", "Compte destinataire introuvable.")),
                ShareError.CannotShareToSelf => BadRequest(new ErrorResponseDto("CANNOT_SHARE_TO_SELF", "Impossible de partager un dossier avec son propre compte.")),
                _ => BadRequest(new ErrorResponseDto("INVALID_REQUEST", "Impossible de partager ce dossier."))
            };
        }

        return Ok(new ShareFolderResponseDto(result.NewFolderId!.Value));
    }

    private static FolderDto ToDto(Domain.Entities.Folder folder) =>
        new(folder.Id, folder.ParentId, folder.Name, folder.IsRoot, folder.Version, folder.UpdatedAt);
}
