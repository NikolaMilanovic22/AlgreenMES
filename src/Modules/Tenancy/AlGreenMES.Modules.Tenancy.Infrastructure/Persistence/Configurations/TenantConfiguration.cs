using System.Text.Json;
using AlGreenMES.Modules.Tenancy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Tenancy.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.LogoUrl)
            .HasMaxLength(500);

        builder.Property(t => t.BlockedReason)
            .HasMaxLength(1000);

        // Serialise List<string> as a JSON text column. Postgres jsonb
        // would also work but text is portable enough for this case
        // (Postgres-only deployments still benefit from server-side
        // indexing if the list ever needs query-time predicates).
        var listComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
            v => v.ToList());

        builder.Property(t => t.DisabledFeatures)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(listComparer);

        builder.Property(t => t.DisabledFeatures)
            .HasColumnType("text")
            .HasDefaultValue(new List<string>());

        builder.HasIndex(t => t.Code)
            .IsUnique();

        builder.HasOne(t => t.Settings)
            .WithOne(s => s.Tenant)
            .HasForeignKey<TenantSettings>(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
