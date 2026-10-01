using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassManager.Api.Common;
using PassManager.Api.Dtos;
using PassManager.Application.Sync;

namespace PassManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sync")]
public class SyncController(SyncService syncService) : ControllerBase
{
    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] DateTimeOffset? since, CancellationToken ct)
    {
        var result = await syncService.PullAsync(this.GetUserId(), since, ct);
        return Ok(result);
    }

    [HttpPost("push")]
    public async Task<IActionResult> Push(PushRequestDto request, CancellationToken ct)
    {
        var folderItems = (request.Folders ?? [])
            .Select(f => new FolderPushItem(f.Id, f.ParentId, f.Name, f.ClientVersion, f.UpdatedAt, f.Deleted))
            .ToList();
        var entryItems = (request.Entries ?? [])
            .Select(e => new EntryPushItem(e.Id, e.FolderId, e.Title, e.Url, e.Login, e.Password, e.Memo, e.ClientVersion, e.UpdatedAt, e.Deleted))
            .ToList();

        var result = await syncService.PushAsync(this.GetUserId(), folderItems, entryItems, ct);
        return Ok(result);
    }
}
