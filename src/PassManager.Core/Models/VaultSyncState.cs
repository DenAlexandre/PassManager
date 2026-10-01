namespace PassManager.Core.Models;

public class VaultSyncState
{
    public DateTimeOffset? LastSyncUtc { get; set; }
    public string? DeviceId { get; set; }
}
