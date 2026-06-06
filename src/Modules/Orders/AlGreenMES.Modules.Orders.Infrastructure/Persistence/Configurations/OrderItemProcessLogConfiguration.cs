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

        // Id is client-generated (TenantEntity sets Guid.NewGuid() in the
        // constructor). Without ValueGeneratedNever() EF expects the DB to
        // generate it and triggers an INSERT-with-read-back path that fails
        // with DbUpdateConcurrencyException ("1 row expected, 0 affected").
        // Same pattern as OrderItemSubProcessLogConfiguration.
        builder.Property(l => l.Id).ValueGeneratedNever();

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
