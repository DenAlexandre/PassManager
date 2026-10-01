using PassManager.Domain.Abstractions;
using PassManager.Domain.Entities;
using PassManager.Domain.Security;

namespace PassManager.Application.Tests.TestDoubles;

public class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

public class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"HASH:{password}";

    public PasswordVerificationResult Verify(string hash, string password) =>
        hash == $"HASH:{password}" ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed;
}

public class FakeTokenService : ITokenService
{
    public int RefreshTokenCounter;

    public string GenerateAccessToken(User user) => $"ACCESS:{user.Id}";

    public GeneratedRefreshToken GenerateRefreshToken()
    {
        var raw = $"REFRESH:{++RefreshTokenCounter}";
        return new GeneratedRefreshToken(raw, TokenHasher.Sha256Hex(raw), DateTimeOffset.UtcNow.AddDays(30));
    }
}

public record SentEmail(string ToEmail, string ConfirmationLink);

public class FakeEmailSender : IEmailSender
{
    public readonly List<SentEmail> Sent = [];

    public Task SendConfirmationEmailAsync(string toEmail, string confirmationLink, CancellationToken ct = default)
    {
        Sent.Add(new SentEmail(toEmail, confirmationLink));
        return Task.CompletedTask;
    }
}
