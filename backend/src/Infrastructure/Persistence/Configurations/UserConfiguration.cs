using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassManager.Domain.Entities;

namespace PassManager.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email)
            .HasColumnType("citext")
            .IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.VaultSalt).IsRequired();
        builder.Property(u => u.EmailConfirmed).IsRequired();
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.UpdatedAt).IsRequired();
    }
}
