using PassManager.Core.Models;

namespace PassManager.Core.Vault;

public class VaultDocument
{
    public List<VaultFolder> Folders { get; set; } = [];
    public List<VaultEntry> Entries { get; set; } = [];
    public List<VaultTombstone> Tombstones { get; set; } = [];
    public VaultSyncState SyncState { get; set; } = new();

    public static VaultDocument CreateEmpty() => new();
}
