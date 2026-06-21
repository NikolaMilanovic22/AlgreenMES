using AlGreenMES.Modules.Tenancy.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Tenancy.Infrastructure.Persistence.Configurations;

public class TenantPaymentConfiguration : IEntityTypeConfiguration<TenantPayment>
{
    public void Configure(EntityTypeBuilder<TenantPayment> builder)
    {
        builder.ToTable("tenant_payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.PeriodStart).IsRequired();
        builder.Property(p => p.PeriodEnd).IsRequired();

        builder.Property(p => p.Amount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(8);

        builder.Property(p => p.PaidAt).IsRequired();

        builder.Property(p => p.InvoiceNumber).HasMaxLength(100);
        builder.Property(p => p.Notes).HasMaxLength(2000);

        builder.Property(p => p.CreatedAt).IsRequired();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.TenantId, p.PaidAt });
    }
}
