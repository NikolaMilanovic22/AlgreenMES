using AlGreenMES.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Identity.Infrastructure.Persistence.Configurations;

public class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        builder.ToTable("login_attempts");

        builder.HasKey(la => la.Id);
        builder.Property(la => la.Id).ValueGeneratedNever();

        // TenantId is nullable on purpose — a login attempt with a wrong
        // tenant code never resolves a tenant; we still want to log it.
        builder.Property(la => la.TenantId);

        builder.Property(la => la.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(la => la.IpAddress)
            .HasMaxLength(64);

        builder.Property(la => la.UserAgent)
            .HasMaxLength(256);

        builder.Property(la => la.Succeeded)
            .IsRequired();

        builder.Property(la => la.FailureReason)
            .HasMaxLength(64);

        builder.Property(la => la.AttemptedAt)
            .IsRequired();

        // Common query shape: "give me failed attempts for email X in the
        // last hour" — covering email + attempted_at sorted desc speeds it up.
        builder.HasIndex(la => new { la.Email, la.AttemptedAt })
            .HasDatabaseName("ix_login_attempts_email_attempted_at");

        // Tenant lookup for forensics: "show me all failed attempts in this
        // company in the last day". TenantId can be null, but Postgres index
        // accepts NULL keys; the filter will skip nulls automatically.
        builder.HasIndex(la => new { la.TenantId, la.AttemptedAt })
            .HasDatabaseName("ix_login_attempts_tenant_attempted_at");
    }
}
