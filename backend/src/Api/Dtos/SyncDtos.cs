using System.ComponentModel.DataAnnotations;

namespace PassManager.Api.Dtos;

public record FolderPushItemDto(
    [Required] Guid Id,
    Guid? ParentId,
    [Required, MinLength(1)] string Name,
    long ClientVersion,
    DateTimeOffset UpdatedAt,
    bool Deleted);

public record EntryPushItemDto(
    [Required] Guid Id,
    [Required] Guid FolderId,
    [Required, MinLength(1)] string Title,
    string? Url,
    string? Login,
    [Required] string Password,
    string? Memo,
    long ClientVersion,
    DateTimeOffset UpdatedAt,
    bool Deleted);

public record PushRequestDto(
    List<FolderPushItemDto>? Folders,
    List<EntryPushItemDto>? Entries);
