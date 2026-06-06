using AlGreenMES.Modules.Orders.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Configurations;

public class OrderItemProcessLogConfiguration : IEntityTypeConfiguration<OrderItemProcessLog>
{
    public void Configure(EntityTypeBuilder<OrderItemProcessLog> builder)
    {
        builder.ToTable("order_item_process_logs");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.OrderItemProcessId).IsRequired();
        builder.Property(l => l.UserId).IsRequired();
        builder.Property(l => l.StartTime).IsRequired();
        builder.Property(l => l.CreatedAt).IsRequired();

        builder.HasIndex(l => l.OrderItemProcessId);
        builder.HasIndex(l => l.UserId);
        builder.HasIndex(l => new { l.TenantId, l.StartTime });

        builder.HasOne(l => l.OrderItemProcess)
            .WithMany(p => p.ProcessLogs)
            .HasForeignKey(l => l.OrderItemProcessId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
