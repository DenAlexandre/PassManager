namespace PassManager.Domain.Entities;

public class Folder
{
    public const string RootName = "Racine";

    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = null!;
    public bool IsRoot { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
