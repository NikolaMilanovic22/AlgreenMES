using System.Reflection;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Entities.OrderTypes;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Orders.Infrastructure.Persistence;

public class OrdersDbContext : DbContext, IOrdersUnitOfWork
{
    private readonly ICurrentUserService _currentUser;

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderType> OrderTypes => Set<OrderType>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderItemProcess> OrderItemProcesses => Set<OrderItemProcess>();
    public DbSet<OrderItemSubProcess> OrderItemSubProcesses => Set<OrderItemSubProcess>();
    public DbSet<OrderItemSpecialRequest> OrderItemSpecialRequests => Set<OrderItemSpecialRequest>();
    public DbSet<OrderItemSubProcessLog> OrderItemSubProcessLogs => Set<OrderItemSubProcessLog>();
    public DbSet<WorkSession> WorkSessions => Set<WorkSession>();
    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<BlockRequest> BlockRequests => Set<BlockRequest>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<OrderAttachment> OrderAttachments => Set<OrderAttachment>();
    public DbSet<OrderManualProcess> OrderManualProcesses => Set<OrderManualProcess>();
    public DbSet<OrderManualProcessDependency> OrderManualProcessDependencies => Set<OrderManualProcessDependency>();

    public OrdersDbContext(DbContextOptions<OrdersDbContext> options, ICurrentUserService currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("orders");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.FindProperty("TenantId") != null)
            {
                typeof(OrdersDbContext)
                    .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(
            e => EF.Property<Guid>(e, "TenantId") == _currentUser.GetCurrentTenantId());
    }
}
