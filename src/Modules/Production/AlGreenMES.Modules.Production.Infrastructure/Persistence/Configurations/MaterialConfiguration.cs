using AlGreenMES.Modules.Production.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Production.Infrastructure.Persistence.Configurations;

public class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("materials");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Code).IsRequired().HasMaxLength(50);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Unit).IsRequired().HasMaxLength(20);
        builder.Property(m => m.Category).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Location).HasMaxLength(100);
        builder.Property(m => m.Notes).HasMaxLength(1000);

        builder.Property(m => m.DimensionX).HasPrecision(18, 3);
        builder.Property(m => m.DimensionY).HasPrecision(18, 3);
        builder.Property(m => m.DimensionZ).HasPrecision(18, 3);

        builder.Property(m => m.IsActive).IsRequired();
        builder.Property(m => m.MinQuantity).IsRequired();
        builder.Property(m => m.MaxQuantity).IsRequired();

        builder.Property(m => m.CreatedAt).IsRequired();

        // Code is unique per tenant — Saša 08.06.2026.
        builder.HasIndex(m => new { m.TenantId, m.Code }).IsUnique();
        builder.HasIndex(m => new { m.TenantId, m.Category });
    }
}
