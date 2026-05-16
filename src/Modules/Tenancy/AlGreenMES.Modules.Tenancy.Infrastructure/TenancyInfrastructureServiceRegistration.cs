using AlGreenMES.BuildingBlocks.Common.Interceptors;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using AlGreenMES.Modules.Tenancy.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AlGreenMES.Modules.Tenancy.Infrastructure;

public static class TenancyInfrastructureServiceRegistration
{
    public static IServiceCollection AddTenancyInfrastructure(this IServiceCollection services, IConfiguration configuration)
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
        services.AddDbContext<TenancyDbContext>((sp, options) =>
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

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TenancyDbContext>());
        services.AddScoped<ITenantRepository, TenantRepository>();

        return services;
    }
}
