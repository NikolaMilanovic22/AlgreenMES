using AlGreenMES.BuildingBlocks.Common.Interceptors;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Infrastructure.Repositories;
using AlGreenMES.Modules.Orders.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AlGreenMES.Modules.Orders.Infrastructure;

public static class OrdersInfrastructureServiceRegistration
{
    public static IServiceCollection AddOrdersInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"))
        {
            MaxPoolSize = 100,
            MinPoolSize = 5,
            ConnectionIdleLifetime = 300,
            ConnectionPruningInterval = 10,
            Timeout = 15,
            CommandTimeout = 30
        }.ConnectionString;

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddDbContext<OrdersDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IOrdersUnitOfWork>(sp => sp.GetRequiredService<OrdersDbContext>());

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderTypeRepository, OrderTypeRepository>();
        services.AddScoped<IOrderItemProcessRepository, OrderItemProcessRepository>();
        services.AddScoped<IOrderItemSubProcessRepository, OrderItemSubProcessRepository>();
        services.AddScoped<IWorkSessionRepository, WorkSessionRepository>();
        services.AddScoped<IChangeRequestRepository, ChangeRequestRepository>();
        services.AddScoped<IBlockRequestRepository, BlockRequestRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
        services.AddScoped<IOrderAttachmentRepository, OrderAttachmentRepository>();

        // File Storage
        services.Configure<FileStorageSettings>(configuration.GetSection("FileStorage"));
        services.AddScoped<IFileStorageService, LocalFileStorageService>();

        // WebPush
        services.Configure<WebPushSettings>(configuration.GetSection("WebPush"));
        services.AddScoped<IWebPushService, WebPushService>();

        services.AddScoped<IDashboardQueryService, DashboardQueryService>();
        services.AddScoped<IReportingQueryService, ReportingQueryService>();

        return services;
    }
}
