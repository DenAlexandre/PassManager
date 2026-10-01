using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PassManager.Application.Common;
using PassManager.Domain.Abstractions;
using PassManager.Domain.Entities;
using PassManager.Domain.Security;

namespace PassManager.Application.Auth;

public class AuthService(
    IPassManagerDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IEmailSender emailSender,
    IClock clock,
    IOptions<AppOptions> appOptions)
{
    private readonly AppOptions _appOptions = appOptions.Value;

    public async Task<RegisterResult> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        email = email.Trim();

        var exists = await db.Users.AnyAsync(u => u.Email == email, ct);
        if (exists)
        {
            return new RegisterResult(false, AuthError.EmailAlreadyRegistered);
        }

        var now = clock.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = passwordHasher.Hash(password),
            VaultSalt = RandomNumberGenerator.GetBytes(Argon2Defaults.SaltSizeBytes),
            EmailConfirmed = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Users.Add(user);

        var rootFolder = new Folder
        {
            Id = Guid.NewGuid(),
            OwnerId = user.Id,
            ParentId = null,
            Name = Folder.RootName,
            IsRoot = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Folders.Add(rootFolder);

        var (rawToken, confirmationToken) = CreateConfirmationToken(user.Id, now);
        db.EmailConfirmationTokens.Add(confirmationToken);

        await db.SaveChangesAsync(ct);

        var link = BuildConfirmationLink(email, rawToken);
        await emailSender.SendConfirmationEmailAsync(email, link, ct);

        return new RegisterResult(true, AuthError.None);
    }

    public async Task<bool> ConfirmEmailAsync(string email, string token, CancellationToken ct = default)
    {
        email = email.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            return false;
        }

        var tokenHash = TokenHasher.Sha256Hex(token);
        var now = clock.UtcNow;
        var confirmationToken = await db.EmailConfirmationTokens
            .Where(t => t.UserId == user.Id && t.TokenHash == tokenHash && t.ConsumedAt == null && t.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);

        if (confirmationToken is null)
        {
            return false;
        }

        confirmationToken.ConsumedAt = now;
        user.EmailConfirmed = true;
        user.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ResendConfirmationAsync(string email, CancellationToken ct = default)
    {
        email = email.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || user.EmailConfirmed)
        {
            return;
        }

        var now = clock.UtcNow;
        var pendingTokens = await db.EmailConfirmationTokens
            .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var pending in pendingTokens)
        {
            pending.ConsumedAt = now;
        }

        var (rawToken, confirmationToken) = CreateConfirmationToken(user.Id, now);
        db.EmailConfirmationTokens.Add(confirmationToken);
        await db.SaveChangesAsync(ct);

        var link = BuildConfirmationLink(email, rawToken);
        await emailSender.SendConfirmationEmailAsync(email, link, ct);
    }

    public async Task<LoginResult> LoginAsync(string email, string password, string? deviceId, CancellationToken ct = default)
    {
        email = email.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            return new LoginResult(false, AuthError.InvalidCredentials, Guid.Empty, null, null, null);
        }

        var verification = passwordHasher.Verify(user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            return new LoginResult(false, AuthError.InvalidCredentials, Guid.Empty, null, null, null);
        }

        if (!user.EmailConfirmed)
        {
            return new LoginResult(false, AuthError.EmailNotConfirmed, Guid.Empty, null, null, null);
        }

        var now = clock.UtcNow;
        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.Hash(password);
            user.UpdatedAt = now;
        }

        var accessToken = tokenService.GenerateAccessToken(user);
        var refresh = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            DeviceId = deviceId,
            ExpiresAt = refresh.ExpiresAt,
            CreatedAt = now
        });

        await db.SaveChangesAsync(ct);

        return new LoginResult(true, AuthError.None, user.Id, accessToken, refresh.RawToken, user.VaultSalt);
    }

    public async Task<RefreshResult> RefreshAsync(string refreshToken, string? deviceId, CancellationToken ct = default)
    {
        var tokenHash = TokenHasher.Sha256Hex(refreshToken);
        var now = clock.UtcNow;
        var existing = await db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash && t.RevokedAt == null && t.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            return new RefreshResult(false, AuthError.InvalidRefreshToken, null, null);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == existing.UserId, ct);
        if (user is null)
        {
            return new RefreshResult(false, AuthError.InvalidRefreshToken, null, null);
        }

        var accessToken = tokenService.GenerateAccessToken(user);
        var newRefresh = tokenService.GenerateRefreshToken();
        var newEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = newRefresh.TokenHash,
            DeviceId = deviceId ?? existing.DeviceId,
            ExpiresAt = newRefresh.ExpiresAt,
            CreatedAt = now
        };
        db.RefreshTokens.Add(newEntity);

        existing.RevokedAt = now;
        existing.ReplacedBy = newEntity.Id;

        await db.SaveChangesAsync(ct);

        return new RefreshResult(true, AuthError.None, accessToken, newRefresh.RawToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var tokenHash = TokenHasher.Sha256Hex(refreshToken);
        var existing = await db.RefreshTokens
            .Where(t => t.TokenHash == tokenHash && t.RevokedAt == null)
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            return;
        }

        existing.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private (string RawToken, EmailConfirmationToken Entity) CreateConfirmationToken(Guid userId, DateTimeOffset now)
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var entity = new EmailConfirmationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = TokenHasher.Sha256Hex(rawToken),
            ExpiresAt = now.AddHours(_appOptions.EmailConfirmationTokenExpiryHours),
            CreatedAt = now
        };

        return (rawToken, entity);
    }

    private string BuildConfirmationLink(string email, string rawToken) =>
        $"{_appOptions.PublicBaseUrl}/api/auth/confirm-email?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(rawToken)}";
}
