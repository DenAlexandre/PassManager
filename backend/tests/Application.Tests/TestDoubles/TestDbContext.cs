using Microsoft.EntityFrameworkCore;
using PassManager.Application.Common;
using PassManager.Domain.Entities;

namespace PassManager.Application.Tests.TestDoubles;

public class TestDbContext : DbContext, IPassManagerDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<EmailConfirmationToken> EmailConfirmationTokens => Set<EmailConfirmationToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<FolderShare> FolderShares => Set<FolderShare>();
    public DbSet<EntryShare> EntryShares => Set<EntryShare>();

    public static TestDbContext Create()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }
}
