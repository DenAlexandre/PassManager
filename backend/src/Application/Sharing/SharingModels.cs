namespace PassManager.Application.Sharing;

public enum ShareError
{
    None,
    FolderNotFound,
    CannotShareRoot,
    TargetUserNotFound,
    CannotShareToSelf
}

public record ShareResult(bool Succeeded, ShareError Error, Guid? NewFolderId);

public record UserSummary(Guid Id, string Email);
