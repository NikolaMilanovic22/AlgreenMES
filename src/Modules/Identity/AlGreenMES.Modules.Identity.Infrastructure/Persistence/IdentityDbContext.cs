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
            var tenantProperty = entityType.FindProperty("TenantId");
            if (tenantProperty == null) continue;
            // Skip entities where TenantId is nullable — those are
            // cross-tenant audit tables (e.g. LoginAttempt where a failed
            // pre-auth attempt has no tenant). The HasQueryFilter expression
            // assumes a non-null Guid, so it can't be applied to Guid?.
            if (Nullable.GetUnderlyingType(tenantProperty.ClrType) != null) continue;

            typeof(IdentityDbContext)
                .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, new object[] { modelBuilder });
        }
    }

    private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(
            e => EF.Property<Guid>(e, "TenantId") == _currentUser.GetCurrentTenantId());
    }
}
