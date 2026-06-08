using AlGreenMES.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlGreenMES.Modules.Identity.Infrastructure.Persistence.Configurations;

public class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("user_role_assignments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.CreatedAt).IsRequired();

        // One row per (user, role) — can't assign the same extra role twice.
        builder.HasIndex(x => new { x.UserId, x.Role }).IsUnique();
    }
}
