using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassManager.Domain.Entities;

namespace PassManager.Infrastructure.Persistence.Configurations;

public class FolderShareConfiguration : IEntityTypeConfiguration<FolderShare>
{
    public void Configure(EntityTypeBuilder<FolderShare> builder)
    {
        builder.ToTable("folder_shares");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SharedAt).IsRequired();

        builder.HasIndex(s => s.TargetOwnerId);
        builder.HasIndex(s => s.SourceOwnerId);
    }
}
