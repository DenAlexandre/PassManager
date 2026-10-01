using Microsoft.EntityFrameworkCore;
using PassManager.Domain.Entities;

namespace PassManager.Application.Common;

public interface IPassManagerDbContext
{
    DbSet<User> Users { get; }
    DbSet<EmailConfirmationToken> EmailConfirmationTokens { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Folder> Folders { get; }
    DbSet<Entry> Entries { get; }
    DbSet<FolderShare> FolderShares { get; }
    DbSet<EntryShare> EntryShares { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
