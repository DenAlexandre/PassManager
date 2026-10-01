namespace PassManager.Core.Models;

public class VaultFolder
{
    public const string RootName = "Racine";

    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
    public bool IsRoot { get; set; }
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
