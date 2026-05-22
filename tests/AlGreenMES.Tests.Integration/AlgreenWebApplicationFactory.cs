using System.Text;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Production.Infrastructure.Persistence;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using AlgreenMES.API;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Xunit;

namespace AlGreenMES.Tests.Integration;

public class AlgreenWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("algreen_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
        await base.DisposeAsync();
    }

    private const string TestJwtSecret = "TEST_SECRET_KEY_AT_LEAST_32_CHARACTERS_LONG_FOR_HS256";
    private const string TestJwtIssuer = "AlGreenMES";
    private const string TestJwtAudience = "AlGreenMES";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        // UseSetting puts these into the host's settings dictionary which sits
        // ABOVE appsettings.json in the configuration precedence chain. The
        // previous ConfigureAppConfiguration.AddInMemoryCollection approach
        // was being overridden by the API's own appsettings.json (default
        // ConnectionStrings:DefaultConnection points at localhost:5432), so
        // tests connected to that instead of the Testcontainers dynamic port.
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
        builder.UseSetting("JwtSettings:Secret", TestJwtSecret);
        builder.UseSetting("JwtSettings:Issuer", TestJwtIssuer);
        builder.UseSetting("JwtSettings:Audience", TestJwtAudience);
        builder.UseSetting("JwtSettings:ExpirationMinutes", "60");

        // AddJwtBearer captures Secret/Issuer/Audience at builder time, before WAF's
        // in-memory config overrides take effect. PostConfigure pins the validation
        // params to the same values JwtTokenService signs with at runtime.
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.IssuerSigningKey =
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
                options.TokenValidationParameters.ValidIssuer = TestJwtIssuer;
                options.TokenValidationParameters.ValidAudience = TestJwtAudience;
            });
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<TenancyDbContext>().Database.Migrate();
        sp.GetRequiredService<IdentityDbContext>().Database.Migrate();
        sp.GetRequiredService<ProductionDbContext>().Database.Migrate();
        sp.GetRequiredService<OrdersDbContext>().Database.Migrate();

        return host;
    }
}
