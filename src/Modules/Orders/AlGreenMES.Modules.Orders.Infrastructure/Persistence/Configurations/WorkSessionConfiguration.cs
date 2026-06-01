using AlGreenMES.Modules.Orders.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence.Configurations;

public class WorkSessionConfiguration : IEntityTypeConfiguration<WorkSession>
{
    public void Configure(EntityTypeBuilder<WorkSession> builder)
    {
        builder.ToTable("work_sessions");

        builder.HasKey(ws => ws.Id);

        builder.Property(ws => ws.CheckInTime)
            .IsRequired();

        builder.Property(ws => ws.Date)
            .IsRequired();

        builder.Property(ws => ws.CreatedAt)
            .IsRequired();

        builder.Property(ws => ws.WasAutoClosed)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Ignore(ws => ws.IsActive);

        builder.HasIndex(ws => ws.UserId);
        builder.HasIndex(ws => new { ws.TenantId, ws.Date });
    }
}
