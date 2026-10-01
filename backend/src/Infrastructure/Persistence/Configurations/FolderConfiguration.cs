using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PassManager.Domain.Entities;

namespace PassManager.Infrastructure.Persistence.Configurations;

public class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("folders");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.Name).IsRequired();
        builder.Property(f => f.IsRoot).IsRequired();
        builder.Property(f => f.Version).IsRequired();
        builder.Property(f => f.UpdatedAt).IsRequired();
        builder.Property(f => f.CreatedAt).IsRequired();

        builder.HasIndex(f => f.OwnerId);
        builder.HasIndex(f => f.ParentId);
        builder.HasIndex(f => new { f.OwnerId, f.UpdatedAt });
        builder.HasIndex(f => f.OwnerId)
            .IsUnique()
            .HasFilter("\"IsRoot\"")
            .HasDatabaseName("ux_folders_one_root_per_user");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Folder>()
            .WithMany()
            .HasForeignKey(f => f.ParentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
