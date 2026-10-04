using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PassManager.Application.Auth;
using PassManager.Application.Common;
using PassManager.Application.Tests.TestDoubles;
using PassManager.Domain.Entities;
using Xunit;

namespace PassManager.Application.Tests.Auth;

public class AuthServiceTests
{
    private static (AuthService Service, TestDbContext Db, FakeEmailSender EmailSender, FakeClock Clock, FakeTokenService TokenService) CreateSut()
    {
        var db = TestDbContext.Create();
        var emailSender = new FakeEmailSender();
        var clock = new FakeClock();
        var tokenService = new FakeTokenService();
        var appOptions = Options.Create(new AppOptions { PublicBaseUrl = "https://app.test", EmailConfirmationTokenExpiryHours = 24 });

        var service = new AuthService(db, new FakePasswordHasher(), tokenService, emailSender, clock, appOptions);
        return (service, db, emailSender, clock, tokenService);
    }

    [Fact]
    public async Task RegisterAsync_CreatesUserAndRootFolder_AndSendsConfirmationEmail()
    {
        var (service, db, emailSender, _, _) = CreateSut();

        var result = await service.RegisterAsync("alice@example.com", "password123");

        Assert.True(result.Succeeded);

        var user = await db.Users.SingleAsync();
        Assert.Equal("alice@example.com", user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal(16, user.VaultSalt.Length);

        var rootFolder = await db.Folders.SingleAsync();
        Assert.Equal("alice@example.com", rootFolder.Name);
        Assert.True(rootFolder.IsRoot);
        Assert.Equal(user.Id, rootFolder.OwnerId);
        Assert.Null(rootFolder.ParentId);

        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal("alice@example.com", sent.ToEmail);
        Assert.Contains("https://app.test/api/auth/confirm-email", sent.ConfirmationLink);
    }

    [Fact]
    public async Task RegisterAsync_WhenEmailAlreadyExists_DoesNotCreateDuplicateOrSendEmail()
    {
        var (service, db, emailSender, _, _) = CreateSut();
        await service.RegisterAsync("bob@example.com", "password123");
        emailSender.Sent.Clear();

        var result = await service.RegisterAsync("bob@example.com", "anotherPassword");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.EmailAlreadyRegistered, result.Error);
        Assert.Single(await db.Users.ToListAsync());
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task ConfirmEmailAsync_WithValidToken_ConfirmsUser()
    {
        var (service, db, emailSender, _, _) = CreateSut();
        await service.RegisterAsync("carol@example.com", "password123");
        var token = ExtractToken(emailSender.Sent[0].ConfirmationLink);

        var confirmed = await service.ConfirmEmailAsync("carol@example.com", token);

        Assert.True(confirmed);
        var user = await db.Users.SingleAsync();
        Assert.True(user.EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmailAsync_WithWrongToken_ReturnsFalseAndLeavesUserUnconfirmed()
    {
        var (service, db, _, _, _) = CreateSut();
        await service.RegisterAsync("dave@example.com", "password123");

        var confirmed = await service.ConfirmEmailAsync("dave@example.com", "not-the-right-token");

        Assert.False(confirmed);
        var user = await db.Users.SingleAsync();
        Assert.False(user.EmailConfirmed);
    }

    [Fact]
    public async Task LoginAsync_BeforeConfirmation_ReturnsEmailNotConfirmed()
    {
        var (service, _, _, _, _) = CreateSut();
        await service.RegisterAsync("erin@example.com", "password123");

        var result = await service.LoginAsync("erin@example.com", "password123", deviceId: null);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.EmailNotConfirmed, result.Error);
    }

    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsInvalidCredentials()
    {
        var (service, _, emailSender, _, _) = CreateSut();
        await service.RegisterAsync("frank@example.com", "password123");
        await service.ConfirmEmailAsync("frank@example.com", ExtractToken(emailSender.Sent[0].ConfirmationLink));

        var result = await service.LoginAsync("frank@example.com", "wrong-password", deviceId: null);

        Assert.False(result.Succeeded);
        Assert.Equal(AuthError.InvalidCredentials, result.Error);
    }

    [Fact]
    public async Task LoginAsync_AfterConfirmation_ReturnsTokensAndVaultSalt()
    {
        var (service, db, emailSender, _, _) = CreateSut();
        await service.RegisterAsync("grace@example.com", "password123");
        await service.ConfirmEmailAsync("grace@example.com", ExtractToken(emailSender.Sent[0].ConfirmationLink));

        var result = await service.LoginAsync("grace@example.com", "password123", deviceId: "device-1");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
        var user = await db.Users.SingleAsync();
        Assert.Equal(user.VaultSalt, result.VaultSalt);

        var storedRefreshToken = await db.RefreshTokens.SingleAsync();
        Assert.Equal("device-1", storedRefreshToken.DeviceId);
        Assert.Null(storedRefreshToken.RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_RotatesToken_AndInvalidatesOldOne()
    {
        var (service, db, emailSender, _, _) = CreateSut();
        await service.RegisterAsync("heidi@example.com", "password123");
        await service.ConfirmEmailAsync("heidi@example.com", ExtractToken(emailSender.Sent[0].ConfirmationLink));
        var login = await service.LoginAsync("heidi@example.com", "password123", deviceId: null);

        var refreshed = await service.RefreshAsync(login.RefreshToken!, deviceId: null);

        Assert.True(refreshed.Succeeded);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        var reused = await service.RefreshAsync(login.RefreshToken!, deviceId: null);
        Assert.False(reused.Succeeded);
        Assert.Equal(AuthError.InvalidRefreshToken, reused.Error);

        Assert.Equal(2, await db.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task LogoutAsync_RevokesRefreshToken()
    {
        var (service, db, emailSender, _, _) = CreateSut();
        await service.RegisterAsync("ivan@example.com", "password123");
        await service.ConfirmEmailAsync("ivan@example.com", ExtractToken(emailSender.Sent[0].ConfirmationLink));
        var login = await service.LoginAsync("ivan@example.com", "password123", deviceId: null);

        await service.LogoutAsync(login.RefreshToken!);

        var stored = await db.RefreshTokens.SingleAsync();
        Assert.NotNull(stored.RevokedAt);

        var afterLogout = await service.RefreshAsync(login.RefreshToken!, deviceId: null);
        Assert.False(afterLogout.Succeeded);
    }

    private static string ExtractToken(string confirmationLink) =>
        Uri.UnescapeDataString(confirmationLink.Split("token=")[1]);
}
