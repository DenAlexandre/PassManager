namespace PassManager.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = null!;
    public string? DeviceId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
