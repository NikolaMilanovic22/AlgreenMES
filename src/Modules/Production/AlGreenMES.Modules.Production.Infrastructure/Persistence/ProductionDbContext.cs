using System.Reflection;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Production.Application.Interfaces;
using AlGreenMES.Modules.Production.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Production.Infrastructure.Persistence;

public class ProductionDbContext : DbContext, IProductionUnitOfWork
{
    private readonly ICurrentUserService _currentUser;

    public DbSet<Process> Processes => Set<Process>();
    public DbSet<SubProcess> SubProcesses => Set<SubProcess>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<ProductCategoryProcess> ProductCategoryProcesses => Set<ProductCategoryProcess>();
    public DbSet<ProductCategoryDependency> ProductCategoryDependencies => Set<ProductCategoryDependency>();
    public DbSet<SpecialRequestType> SpecialRequestTypes => Set<SpecialRequestType>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public ProductionDbContext(DbContextOptions<ProductionDbContext> options, ICurrentUserService currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public void ClearChangeTracker() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("production");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProductionDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.FindProperty("TenantId") != null)
            {
                typeof(ProductionDbContext)
                    .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class
    {
        // EF.Property<Guid?> so the filter still applies after TenantEntity
        // turned its TenantId column into Guid? on 16.06.2026 — the strongly-
        // typed Guid version silently became a no-op for every TenantEntity
        // child (Saša 19.06.2026: cross-tenant data leak in Shifts caught
        // this; same hole existed for Orders / Production rows).
        modelBuilder.Entity<TEntity>().HasQueryFilter(
            e => EF.Property<Guid?>(e, "TenantId") == _currentUser.GetCurrentTenantId());
    }
}
