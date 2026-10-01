using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PassManager.Domain.Entities;
using PassManager.Infrastructure.Crypto;

namespace PassManager.Infrastructure.Persistence.Configurations;

public class EntryConfiguration(EnvelopeEncryptionService envelopeEncryption) : IEntityTypeConfiguration<Entry>
{
    public void Configure(EntityTypeBuilder<Entry> builder)
    {
        builder.ToTable("entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Title).IsRequired();
        builder.Property(e => e.Version).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.Property(e => e.Password)
            .HasConversion(new ValueConverter<string, string>(
                plain => envelopeEncryption.Encrypt(plain),
                stored => envelopeEncryption.Decrypt(stored)))
            .IsRequired();

        builder.Property(e => e.Memo)
            .HasConversion(new ValueConverter<string?, string>(
                plain => envelopeEncryption.Encrypt(plain!),
                stored => envelopeEncryption.Decrypt(stored)));

        builder.HasIndex(e => e.FolderId);
        builder.HasIndex(e => new { e.OwnerId, e.UpdatedAt });

        builder.HasOne<Folder>()
            .WithMany()
            .HasForeignKey(e => e.FolderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
