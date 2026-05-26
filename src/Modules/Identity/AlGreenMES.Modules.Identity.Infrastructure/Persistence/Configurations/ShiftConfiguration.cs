using AlGreenMES.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Identity.Infrastructure.Persistence.Configurations;

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("shifts");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.StartTime)
            .IsRequired();

        builder.Property(s => s.EndTime)
            .IsRequired();

        builder.Property(s => s.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Per-shift time-tracking config (Bojan spec 25.05.2026).
        // Defaults match Bojan's stated values: 0 min break (configurable),
        // 6h max overtime, auto-logout every 2h, 5 min alarm before logout.
        builder.Property(s => s.BreakMinutes)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.MaxOvertimeHours)
            .IsRequired()
            .HasDefaultValue(6);

        builder.Property(s => s.AutoLogoutAfterHours)
            .IsRequired()
            .HasDefaultValue(2);

        builder.Property(s => s.AlarmBeforeLogoutMinutes)
            .IsRequired()
            .HasDefaultValue(5);

        builder.Property(s => s.CreatedAt)
            .IsRequired();
    }
}
