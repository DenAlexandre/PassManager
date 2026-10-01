using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassManager.Domain.Entities;

namespace PassManager.Infrastructure.Persistence.Configurations;

public class EmailConfirmationTokenConfiguration : IEntityTypeConfiguration<EmailConfirmationToken>
{
    public void Configure(EntityTypeBuilder<EmailConfirmationToken> builder)
    {
        builder.ToTable("email_confirmation_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasIndex(t => t.UserId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
