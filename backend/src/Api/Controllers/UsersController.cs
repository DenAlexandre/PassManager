using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PassManager.Api.Common;
using PassManager.Api.Dtos;
using PassManager.Application.Sharing;

namespace PassManager.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController(FolderSharingService sharingService) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? query, CancellationToken ct)
    {
        var results = await sharingService.SearchUsersAsync(this.GetUserId(), query, ct);
        return Ok(results.Select(r => new UserSummaryDto(r.Id, r.Email)));
    }
}
