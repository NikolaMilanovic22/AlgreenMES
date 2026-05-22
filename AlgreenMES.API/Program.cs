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
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System.Text.Json;

namespace AlgreenMES.API;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Bootstrap logger — captures startup failures before the host is built.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(new CompactJsonFormatter())
            .CreateBootstrapLogger();

        // --migrate: apply pending EF Core migrations for all 4 DbContexts then
        // exit. Invoked by deploy.sh before the systemd service restart so
        // migrations run as an explicit deploy step instead of a startup race
        // (Sprint 3.4). Exits 0 on success, 1 on failure.
        if (args.Contains("--migrate"))
        {
            return await RunMigrationsAsync(args);
        }

        try
        {
            Log.Information("Starting AlGreenMES.API");

            var builder = WebApplication.CreateBuilder(args);

            // Replace default logging with Serilog (Sprint 3.2).
            // Reads sinks/levels from Serilog section of appsettings; file path
            // defaults to ./logs/api-.log but can be overridden per environment.
            builder.Host.UseSerilog((context, services, configuration) =>
            {
                var logPath = context.Configuration["Serilog:LogPath"] ?? "./logs/api-.log";
                EnsureLogDirectoryExists(logPath);

                configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .Enrich.WithEnvironmentName()
                    .Enrich.WithMachineName()
                    .WriteTo.Console(new CompactJsonFormatter())
                    .WriteTo.File(
                        formatter: new CompactJsonFormatter(),
                        path: logPath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        shared: true);

                // Only wire the Sentry sink when a DSN is configured — otherwise the
                // sink calls SentrySdk.Init with a null DSN and throws on startup.
                // UseSentry above handles its own no-op behavior gracefully; the
                // Serilog sink does not.
                var sentryDsn = context.Configuration["Sentry:Dsn"];
                if (!string.IsNullOrWhiteSpace(sentryDsn))
                {
                    configuration.WriteTo.Sentry(o =>
                    {
                        o.Dsn = sentryDsn;
                        o.Environment = context.Configuration["Sentry:Environment"] ?? "development";
                        o.Release = context.Configuration["Sentry:Release"];
                        o.InitializeSdk = false;
                        o.MinimumBreadcrumbLevel = LogEventLevel.Information;
                        o.MinimumEventLevel = LogEventLevel.Error;
                    });
                }
            });

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

            // Health checks (Sprint 3.3).
            // /health/live   = self  → "process is running" (UptimeRobot liveness)
            // /health/ready  = "live" + postgres → "ready to serve traffic"
            var dbConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            builder.Services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live", "ready" })
                .AddNpgSql(
                    connectionString: dbConnectionString!,
                    name: "postgres",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: new[] { "ready", "db" });

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
                    // Drop business-rule exceptions: they're 4xx by design and
                    // GlobalExceptionHandlerMiddleware already returns a structured
                    // error to the client. Shipping them to Sentry just spams the
                    // inbox with non-bugs (e.g. duplicate check-in, not-found,
                    // forbidden, validation failures).
                    if (sentryEvent.Exception is
                        AlGreenMES.BuildingBlocks.Common.Exceptions.DomainException or
                        AlGreenMES.BuildingBlocks.Common.Exceptions.ValidationException or
                        AlGreenMES.BuildingBlocks.Common.Exceptions.NotFoundException or
                        AlGreenMES.BuildingBlocks.Common.Exceptions.ForbiddenException)
                    {
                        return null;
                    }
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
            // UseSerilogRequestLogging sits OUTSIDE auth so it captures every
            // response — including 401/403 short-circuits from UseAuthorization.
            // EnrichDiagnosticContext pulls tenant/user/correlation from
            // HttpContext at log-time (auth has populated HttpContext.User by
            // then). SerilogEnrichmentMiddleware later pushes the same values
            // into LogContext so any logger.X() calls in controllers also see
            // them.
            app.UseSerilogRequestLogging(options =>
            {
                options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                {
                    var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                        ?? httpContext.Response.Headers["X-Correlation-Id"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(correlationId))
                    {
                        diagnosticContext.Set("CorrelationId", correlationId);
                    }
                    if (httpContext.User.Identity?.IsAuthenticated == true)
                    {
                        try
                        {
                            var tenantService = httpContext.RequestServices.GetService<ITenantService>();
                            var tenantId = tenantService?.GetCurrentTenantId();
                            if (tenantId.HasValue && tenantId.Value != Guid.Empty)
                            {
                                diagnosticContext.Set("TenantId", tenantId.Value);
                            }
                        }
                        catch { /* tenant not resolvable */ }
                        try
                        {
                            var userService = httpContext.RequestServices.GetService<ICurrentUserService>();
                            var userId = userService?.GetCurrentUserId();
                            if (userId.HasValue && userId.Value != Guid.Empty)
                            {
                                diagnosticContext.Set("UserId", userId.Value);
                            }
                        }
                        catch { /* user not resolvable */ }
                    }
                };
            });
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<SerilogEnrichmentMiddleware>();
            app.UseMiddleware<SentryEnrichmentMiddleware>();
            app.MapControllers();
            app.MapHub<ProductionHub>("/hubs/production");

            // Health endpoints — anonymous, sanitized JSON (no stack traces / no
            // connection strings). Mounted under /api/* so the existing nginx
            // proxy rule routes them to the backend without config changes.
            app.MapHealthChecks("/api/health/live", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live"),
                ResponseWriter = WriteHealthResponse,
                AllowCachingResponses = false
            });
            app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = WriteHealthResponse,
                AllowCachingResponses = false
            });

            // Migrations no longer run on startup (Sprint 3.4) — deploy.sh
            // invokes `dotnet AlgreenMES.API.dll --migrate` as an explicit step
            // before restarting the service. In development, run migrations
            // manually: `dotnet run --project AlgreenMES.API -- --migrate`.

            // Seed demo data (testing phase — runs in all environments except Test)
            if (!app.Environment.IsEnvironment("Test"))
            {
                await DataSeeder.SeedAsync(app.Services);
            }

            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "AlGreenMES.API terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task<int> RunMigrationsAsync(string[] args)
    {
        try
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            // DbContexts depend on ICurrentUserService / ITenantService for
            // tenant filtering and audit. Migrations don't actually exercise
            // those filters but DI must still resolve the dependency graph.
            // HttpContextAccessor returns a null context off the request path,
            // which both services tolerate.
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
            builder.Services.AddScoped<ITenantService, TenantService>();

            // Same module wiring as the API so DbContext configuration
            // (pooling, naming convention, retry) is identical between
            // migration and runtime.
            builder.Services.AddTenancyModule(builder.Configuration);
            builder.Services.AddIdentityModule(builder.Configuration);
            builder.Services.AddProductionModule(builder.Configuration);
            builder.Services.AddOrdersModule(builder.Configuration);

            var app = builder.Build();
            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;

            await ApplyMigrationsAsync<TenancyDbContext>(sp, "Tenancy");
            await ApplyMigrationsAsync<IdentityDbContext>(sp, "Identity");
            await ApplyMigrationsAsync<ProductionDbContext>(sp, "Production");
            await ApplyMigrationsAsync<OrdersDbContext>(sp, "Orders");

            Log.Information("All migrations applied successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Migration runner failed.");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task ApplyMigrationsAsync<TDbContext>(IServiceProvider sp, string moduleName)
        where TDbContext : DbContext
    {
        var db = sp.GetRequiredService<TDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            Log.Information("{Module}: no pending migrations.", moduleName);
            return;
        }
        Log.Information("{Module}: applying {Count} migration(s): {Pending}",
            moduleName, pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync();
        Log.Information("{Module}: applied {Count} migration(s).", moduleName, pending.Count);
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds,
                error = e.Value.Exception?.Message
            })
        });
        return context.Response.WriteAsync(payload);
    }

    private static void EnsureLogDirectoryExists(string logPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch (Exception ex)
        {
            // Log dir creation must never crash boot — fall back to console-only.
            Log.Warning(ex, "Could not create log directory for {LogPath}", logPath);
        }
    }
}
