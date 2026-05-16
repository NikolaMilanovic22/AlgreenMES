using AlGreenMES.BuildingBlocks.Common.Interceptors;
using AlGreenMES.Modules.Production.Application.Interfaces;
using AlGreenMES.Modules.Production.Domain.Repositories;
using AlGreenMES.Modules.Production.Infrastructure.Persistence;
using AlGreenMES.Modules.Production.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AlGreenMES.Modules.Production.Infrastructure;

public static class ProductionInfrastructureServiceRegistration
{
    public static IServiceCollection AddProductionInfrastructure(this IServiceCollection services, IConfiguration configuration)
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

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddDbContext<ProductionDbContext>((sp, options) =>
        {
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IProductionUnitOfWork>(sp => sp.GetRequiredService<ProductionDbContext>());

        services.AddScoped<IProcessRepository, ProcessRepository>();
        services.AddScoped<IProductCategoryRepository, ProductCategoryRepository>();
        services.AddScoped<ISpecialRequestTypeRepository, SpecialRequestTypeRepository>();

        return services;
    }
}
