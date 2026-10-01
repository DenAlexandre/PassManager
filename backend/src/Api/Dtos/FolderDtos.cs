using System.ComponentModel.DataAnnotations;

namespace PassManager.Api.Dtos;

public record FolderDto(Guid Id, Guid? ParentId, string Name, bool IsRoot, long Version, DateTimeOffset UpdatedAt);

public record CreateFolderRequestDto(
    [Required] Guid Id,
    [Required] Guid ParentId,
    [Required, MinLength(1)] string Name);

public record RenameFolderRequestDto([Required, MinLength(1)] string Name);
