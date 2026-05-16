using System.Text;
using System.Text.Json.Serialization;
using AlgreenMES.API.Middleware;
using AlgreenMES.API.Services;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Identity.Api;
using AlGreenMES.Modules.Orders.Api;
using AlGreenMES.Modules.Orders.Api.Hubs;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Production.Api;
using AlGreenMES.Modules.Production.Infrastructure.Persistence;
using AlGreenMES.Modules.Tenancy.Api;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace AlgreenMES.API;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
        builder.Services.AddOpenApi();
        builder.Services.AddHttpContextAccessor();

        // JWT Authentication
        var jwtSettings = builder.Configuration.GetSection("JwtSettings");
        var secret = jwtSettings["Secret"]!;

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidAudience = jwtSettings["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
            };

            // Allow JWT token via query string for SignalR
            options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) &&
                        (path.StartsWithSegments("/hubs") || path.Value?.Contains("/attachments/") == true))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireSuperAdmin", policy =>
                policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "SuperAdmin"));
        });

        // Current user / tenant resolution from JWT
        builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
        builder.Services.AddScoped<ITenantService, TenantService>();

        // SignalR event service
        builder.Services.AddScoped<IProductionEventService, ProductionEventService>();
        builder.Services.AddScoped<IProcessChangeNotifier, ProcessChangeNotifier>();
        builder.Services.AddScoped<AlGreenMES.Modules.Production.Application.Interfaces.IReferenceCheckService, ReferenceCheckService>();

        // Background services
        builder.Services.AddHostedService<DeadlineWarningService>();

        // Module registrations
        builder.Services.AddTenancyModule(builder.Configuration);
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddProductionModule(builder.Configuration);
        builder.Services.AddOrdersModule(builder.Configuration);

        // Mapster
        builder.Services.AddSingleton(TypeAdapterConfig.GlobalSettings);
        builder.Services.AddScoped<IMapper, ServiceMapper>();

        // CORS
        const string CorsPolicyName = "AlgreenMesCors";

        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        if (allowedOrigins.Length == 0)
        {
            throw new InvalidOperationException(
                "CORS configuration error: Cors:AllowedOrigins is empty. " +
                "Add at least one allowed origin to appsettings.");
        }

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            });
        });

        // Sentry — error tracking with per-tenant/user tagging (Sprint 3.1).
        // DSN/environment/release come from appsettings.{env}.json (or env vars
        // with Sentry__Dsn etc.). Empty DSN at runtime = SDK no-ops cleanly.
        builder.WebHost.UseSentry(options =>
        {
            options.Dsn = builder.Configuration["Sentry:Dsn"];
            options.Environment = builder.Configuration["Sentry:Environment"] ?? "development";
            options.Release = builder.Configuration["Sentry:Release"];
            options.TracesSampleRate = 0.1;
            options.SendDefaultPii = false;
            options.AttachStacktrace = true;
            options.MaxBreadcrumbs = 100;
            options.SetBeforeSend((sentryEvent, hint) =>
            {
                if (sentryEvent.Request?.Headers != null)
                {
                    sentryEvent.Request.Headers.Remove("Authorization");
                    sentryEvent.Request.Headers.Remove("Cookie");
                }
                return sentryEvent;
            });
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        app.UseSentryTracing();
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();
        app.UseCors(CorsPolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<SentryEnrichmentMiddleware>();
        app.MapControllers();
        app.MapHub<ProductionHub>("/hubs/production");

        // Auto-migrate on startup
        {
            using var migrationScope = app.Services.CreateScope();
            var sp = migrationScope.ServiceProvider;
            await sp.GetRequiredService<TenancyDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<ProductionDbContext>().Database.MigrateAsync();
            await sp.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
        }

        // Seed demo data (testing phase — runs in all environments except Test)
        if (!app.Environment.IsEnvironment("Test"))
        {
            await DataSeeder.SeedAsync(app.Services);
        }

        app.Run();
    }
}
