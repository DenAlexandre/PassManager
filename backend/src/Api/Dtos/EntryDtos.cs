using System.ComponentModel.DataAnnotations;

namespace PassManager.Api.Dtos;

public record EntryDto(
    Guid Id, Guid FolderId, string Title, string? Url, string? Login, string Password, string? Memo,
    long Version, DateTimeOffset UpdatedAt);

public record CreateEntryRequestDto(
    [Required] Guid Id,
    [Required] Guid FolderId,
    [Required, MinLength(1)] string Title,
    string? Url,
    string? Login,
    [Required] string Password,
    string? Memo);

public record UpdateEntryRequestDto(
    [Required, MinLength(1)] string Title,
    string? Url,
    string? Login,
    [Required] string Password,
    string? Memo);
