namespace PassManager.Infrastructure.Auth;

public class JwtOptions
{
    public string SigningKey { get; set; } = null!;
    public string Issuer { get; set; } = "passmanager-api";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
