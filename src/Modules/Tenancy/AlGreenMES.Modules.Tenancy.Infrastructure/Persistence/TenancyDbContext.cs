using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Tenancy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;

public class TenancyDbContext : DbContext, IUnitOfWork
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();

    public TenancyDbContext(DbContextOptions<TenancyDbContext> options, ICurrentUserService currentUser)
        : base(options)
    {
        // currentUser is no longer needed here — see OnModelCreating note.
        _ = currentUser;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("tenancy");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenancyDbContext).Assembly);

        // No global tenant filter here. Tenancy is a SuperAdmin-only module
        // (TenantsController has [Authorize(Policy = "RequireSuperAdmin")]) whose
        // purpose is to manage tenants and their settings across tenant boundaries.
        // Applying the Sprint 2.4a HasQueryFilter would hide rows for any tenant
        // other than the SuperAdmin's home tenant, breaking the cross-tenant flow.
    }
}
