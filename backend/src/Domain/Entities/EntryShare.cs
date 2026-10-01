namespace PassManager.Domain.Entities;

public class EntryShare
{
    public Guid Id { get; set; }
    public Guid SourceEntryId { get; set; }
    public Guid SourceOwnerId { get; set; }
    public Guid TargetOwnerId { get; set; }
    public DateTimeOffset SharedAt { get; set; }
}
