using AlGreenMES.Modules.Orders.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Configurations;

public class OrderManualProcessDependencyConfiguration : IEntityTypeConfiguration<OrderManualProcessDependency>
{
    public void Configure(EntityTypeBuilder<OrderManualProcessDependency> builder)
    {
        builder.ToTable("order_manual_process_dependencies");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderId).IsRequired();
        builder.Property(x => x.ProcessId).IsRequired();
        builder.Property(x => x.DependsOnProcessId).IsRequired();

        builder.HasIndex(x => new { x.OrderId, x.ProcessId, x.DependsOnProcessId }).IsUnique();
        builder.HasIndex(x => x.OrderId);
    }
}
