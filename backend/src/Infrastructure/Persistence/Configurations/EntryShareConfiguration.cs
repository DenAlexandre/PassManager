using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassManager.Domain.Entities;

namespace PassManager.Infrastructure.Persistence.Configurations;

public class EntryShareConfiguration : IEntityTypeConfiguration<EntryShare>
{
    public void Configure(EntityTypeBuilder<EntryShare> builder)
    {
        builder.ToTable("entry_shares");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SharedAt).IsRequired();

        builder.HasIndex(s => s.TargetOwnerId);
        builder.HasIndex(s => s.SourceOwnerId);
    }
}
