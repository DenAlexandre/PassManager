namespace PassManager.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public byte[] VaultSalt { get; set; } = null!;
    public bool EmailConfirmed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
