using PassManager.Domain.Entities;

namespace PassManager.Application.Folders;

public enum FolderError
{
    None,
    NotFound,
    ParentNotFound,
    CannotModifyRoot,
    DuplicateId
}

public record FolderResult(bool Succeeded, FolderError Error, Folder? Folder);
