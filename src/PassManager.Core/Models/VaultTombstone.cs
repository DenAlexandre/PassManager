namespace PassManager.Core.Models;

public enum TombstoneType
{
    Folder,
    Entry
}

public class VaultTombstone
{
    public Guid Id { get; set; }
    public TombstoneType Type { get; set; }

    /// <summary>The item's last known server version before deletion (0 if it was created locally and never synced).</summary>
    public long Version { get; set; }

    public DateTimeOffset DeletedAt { get; set; }
}
