using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PassManager.Domain.Abstractions;
using PassManager.Domain.Entities;
using PassManager.Domain.Security;

namespace PassManager.Infrastructure.Auth;

public class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public GeneratedRefreshToken GenerateRefreshToken()
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tokenHash = TokenHasher.Sha256Hex(rawToken);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays);

        return new GeneratedRefreshToken(rawToken, tokenHash, expiresAt);
    }
}
