using System.ComponentModel.DataAnnotations;

namespace PassManager.Api.Dtos;

public record ShareFolderRequestDto([Required] Guid TargetUserId);

public record ShareFolderResponseDto(Guid NewFolderId);

public record UserSummaryDto(Guid Id, string Email);
