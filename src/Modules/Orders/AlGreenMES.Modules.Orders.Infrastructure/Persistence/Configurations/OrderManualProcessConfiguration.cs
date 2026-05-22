using AlGreenMES.Modules.Orders.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Configurations;

public class OrderManualProcessConfiguration : IEntityTypeConfiguration<OrderManualProcess>
{
    public void Configure(EntityTypeBuilder<OrderManualProcess> builder)
    {
        builder.ToTable("order_manual_processes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.ProcessId).IsRequired();
        builder.Property(x => x.SequenceOrder).IsRequired();

        builder.Property(x => x.DefaultComplexity)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(x => new { x.OrderId, x.ProcessId }).IsUnique();
        builder.HasIndex(x => x.OrderId);
    }
}
