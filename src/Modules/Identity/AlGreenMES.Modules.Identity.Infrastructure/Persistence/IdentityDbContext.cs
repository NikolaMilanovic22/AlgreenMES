using System.Reflection;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Identity.Infrastructure.Persistence;

public class IdentityDbContext : DbContext, IIdentityUnitOfWork
{
    private readonly ICurrentUserService _currentUser;

    public DbSet<User> Users => Set<User>();
    public DbSet<UserProcess> UserProcesses => Set<UserProcess>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<UserRoleChangeLog> UserRoleChangeLogs => Set<UserRoleChangeLog>();

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, ICurrentUserService currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("identity");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.FindProperty("TenantId") == null) continue;
            // LoginAttempt is intentionally NOT tenant-filtered — a failed
            // login with an unknown tenant code can't resolve a tenant, and
            // we still want the row in the audit table (see the class doc on
            // LoginAttempt). Pre-auth queries against this table run from a
            // scope with no tenant claim, so any filter would drop every row.
            if (entityType.ClrType == typeof(LoginAttempt)) continue;
            // Apply the filter to every remaining entity with a TenantId
            // column, including User (TenantId became Guid? on 16.06.2026
            // when SuperAdmins went tenantless). Skipping nullable TenantId
            // silently turned the filter off for every TenantEntity child
            // (Saša 19.06.2026: cross-tenant Shift writes leaked through this
            // hole until the audit caught it). SA lookup sites use
            // IgnoreQueryFilters() where they need to see tenantless rows.
            typeof(IdentityDbContext)
                .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, new object[] { modelBuilder });
        }
    }

    private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class
    {
        // Read the column as Guid? so the comparison compiles for both
        // non-null tenant columns (most entities) and nullable User.
        // A row with TenantId == null never matches GetCurrentTenantId
        // (which throws on missing claim or returns Guid.Empty), so SAs
        // are correctly filtered out of tenant-scoped queries by default.
        modelBuilder.Entity<TEntity>().HasQueryFilter(
            e => EF.Property<Guid?>(e, "TenantId") == _currentUser.GetCurrentTenantId());
    }
}
