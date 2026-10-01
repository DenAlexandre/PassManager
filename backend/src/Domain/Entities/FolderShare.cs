namespace PassManager.Domain.Entities;

public class FolderShare
{
    public Guid Id { get; set; }
    public Guid SourceFolderId { get; set; }
    public Guid SourceOwnerId { get; set; }
    public Guid TargetOwnerId { get; set; }
    public DateTimeOffset SharedAt { get; set; }
}
