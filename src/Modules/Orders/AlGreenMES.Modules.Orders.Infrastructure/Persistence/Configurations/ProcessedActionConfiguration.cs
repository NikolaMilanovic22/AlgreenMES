using AlGreenMES.Modules.Orders.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Configurations;

public class ProcessedActionConfiguration : IEntityTypeConfiguration<ProcessedAction>
{
    public void Configure(EntityTypeBuilder<ProcessedAction> builder)
    {
        builder.ToTable("processed_actions");

        builder.HasKey(a => a.Id);

        // Client-generated Id (TenantEntity sets Guid.NewGuid()); see
        // OrderItemProcessLogConfiguration for why ValueGeneratedNever matters.
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.ActionId).IsRequired();
        builder.Property(a => a.ActionType).IsRequired().HasMaxLength(64);
        builder.Property(a => a.ProcessedAt).IsRequired();

        // The idempotency guarantee at the DB level: a second insert of the
        // same ActionId (a racing duplicate) fails rather than double-applying.
        builder.HasIndex(a => a.ActionId).IsUnique();
    }
}
