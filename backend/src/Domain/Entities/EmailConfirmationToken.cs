namespace PassManager.Domain.Entities;

public class EmailConfirmationToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
