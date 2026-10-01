using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PassManager.Application.Auth;
using PassManager.Application.Common;
using PassManager.Application.Entries;
using PassManager.Application.Folders;
using PassManager.Application.Sharing;
using PassManager.Application.Sync;
using PassManager.Domain.Abstractions;
using PassManager.Infrastructure.Auth;
using PassManager.Infrastructure.Common;
using PassManager.Infrastructure.Crypto;
using PassManager.Infrastructure.Email;
using PassManager.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<VaultOptions>(builder.Configuration.GetSection("Vault"));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

builder.Services.AddDbContext<PassManagerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IPassManagerDbContext>(sp => sp.GetRequiredService<PassManagerDbContext>());

builder.Services.AddScoped<IPasswordHasher, PasswordHasherAdapter>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<FolderService>();
builder.Services.AddScoped<EntryService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddScoped<FolderSharingService>();
builder.Services.AddScoped<EntrySharingService>();

var emailProvider = builder.Configuration["Email:Provider"] ?? "Console";
if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender, DevConsoleEmailSender>();
}

var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is required.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "passmanager-api";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtIssuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<PassManagerDbContext>().Database.Migrate();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program;
