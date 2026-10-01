using PassManager.Domain.Entities;

namespace PassManager.Domain.Abstractions;

public record GeneratedRefreshToken(string RawToken, string TokenHash, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    string GenerateAccessToken(User user);
    GeneratedRefreshToken GenerateRefreshToken();
}
