namespace PassManager.Domain.Entities;

public class Entry
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public Guid OwnerId { get; set; }
    public string Title { get; set; } = null!;
    public string? Url { get; set; }
    public string? Login { get; set; }
    public string Password { get; set; } = null!;
    public string? Memo { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
