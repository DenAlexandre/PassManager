using PassManager.Domain.Entities;

namespace PassManager.Application.Entries;

public enum EntryError
{
    None,
    NotFound,
    FolderNotFound,
    DuplicateId
}

public record EntryResult(bool Succeeded, EntryError Error, Entry? Entry);
