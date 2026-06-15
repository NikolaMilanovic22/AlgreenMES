using AlGreenMES.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Identity.Infrastructure.Persistence.Configurations;

public class UserRoleChangeLogConfiguration : IEntityTypeConfiguration<UserRoleChangeLog>
{
    public void Configure(EntityTypeBuilder<UserRoleChangeLog> builder)
    {
        builder.ToTable("user_role_change_logs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.UserId).IsRequired();
        builder.Property(l => l.OldRole).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(l => l.NewRole).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(l => l.ChangedByUserId).IsRequired();
        builder.Property(l => l.ChangedAt).IsRequired();
        builder.Property(l => l.Reason); // nullable, no length cap — admin justifications can be long

        // Lookup pattern: "every change to user X, newest first" — the
        // forensic UX. Covers the typical question without needing other
        // indexes; the tenant query filter (HasQueryFilter on TenantId)
        // already prunes by tenant.
        builder.HasIndex(l => new { l.UserId, l.ChangedAt })
            .HasDatabaseName("ix_user_role_change_logs_user_changed_at");
    }
}
