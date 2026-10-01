using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PassManager.Application.Common;
using PassManager.Domain.Entities;
using PassManager.Infrastructure.Crypto;
using PassManager.Infrastructure.Persistence.Configurations;

namespace PassManager.Infrastructure.Persistence;

public class PassManagerDbContext : DbContext, IPassManagerDbContext
{
    private readonly EnvelopeEncryptionService _envelopeEncryption;

    public PassManagerDbContext(DbContextOptions<PassManagerDbContext> options, IOptions<VaultOptions> vaultOptions)
        : base(options)
    {
        _envelopeEncryption = new EnvelopeEncryptionService(vaultOptions);
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<EmailConfirmationToken> EmailConfirmationTokens => Set<EmailConfirmationToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<FolderShare> FolderShares => Set<FolderShare>();
    public DbSet<EntryShare> EntryShares => Set<EntryShare>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new EmailConfirmationTokenConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new FolderConfiguration());
        modelBuilder.ApplyConfiguration(new EntryConfiguration(_envelopeEncryption));
        modelBuilder.ApplyConfiguration(new FolderShareConfiguration());
        modelBuilder.ApplyConfiguration(new EntryShareConfiguration());
    }
}
