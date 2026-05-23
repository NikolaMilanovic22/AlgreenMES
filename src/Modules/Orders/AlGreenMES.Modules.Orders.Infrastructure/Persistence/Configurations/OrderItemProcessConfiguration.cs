using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Configurations;

public class OrderItemProcessConfiguration : IEntityTypeConfiguration<OrderItemProcess>
{
    public void Configure(EntityTypeBuilder<OrderItemProcess> builder)
    {
        builder.ToTable("order_item_processes");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Complexity)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.ComplexityOverridden)
            .IsRequired();

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(p => p.TotalDurationMinutes)
            .IsRequired();

        builder.Property(p => p.IsWithdrawn)
            .IsRequired();

        builder.Property(p => p.IsExcludedFromReports)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.BlockReason)
            .HasMaxLength(2000);

        builder.Property(p => p.WithdrawnReason)
            .HasMaxLength(2000);

        builder.Property(p => p.StoppedReason)
            .HasMaxLength(2000);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.HasIndex(p => new { p.OrderItemId, p.ProcessId })
            .IsUnique();

        builder.HasIndex(p => new { p.TenantId, p.Status });

        builder.HasMany(p => p.SubProcesses)
            .WithOne(sp => sp.OrderItemProcess)
            .HasForeignKey(sp => sp.OrderItemProcessId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(OrderItemProcess.SubProcesses))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
