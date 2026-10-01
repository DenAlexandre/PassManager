namespace PassManager.Core.Models;

public class VaultEntry
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public string? Login { get; set; }
    public string Password { get; set; } = "";
    public string? Memo { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
