using AlGreenMES.Modules.Production.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Production.Infrastructure.Persistence.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(s => s.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(s => s.UnitPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.TotalPrice).HasPrecision(18, 2).IsRequired();
        builder.Property(s => s.MovementDate).IsRequired();
        builder.Property(s => s.DocumentReference).IsRequired().HasMaxLength(50);
        builder.Property(s => s.Notes).HasMaxLength(1000);

        builder.Ignore(s => s.SignedQuantity);

        builder.HasOne(s => s.Material)
            .WithMany()
            .HasForeignKey(s => s.MaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Process)
            .WithMany()
            .HasForeignKey(s => s.ProcessId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(s => new { s.TenantId, s.MaterialId });
        builder.HasIndex(s => new { s.TenantId, s.MovementDate });
        builder.HasIndex(s => new { s.TenantId, s.DocumentReference });
    }
}
