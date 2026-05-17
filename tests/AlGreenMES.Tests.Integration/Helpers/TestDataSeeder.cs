using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Production.Domain.Entities;
using AlGreenMES.Modules.Production.Infrastructure.Persistence;
using AlGreenMES.Modules.Tenancy.Domain.Entities;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace AlGreenMES.Tests.Integration.Helpers;

public static class TestDataSeeder
{
    public const string DefaultPassword = "TestPass123!";

    public static async Task<SeededTenant> SeedTenantWithUserAsync(
        AlgreenWebApplicationFactory factory,
        UserRole role = UserRole.Admin,
        string? tenantCodeOverride = null,
        string? emailOverride = null)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var tenancyDb = sp.GetRequiredService<TenancyDbContext>();
        var identityDb = sp.GetRequiredService<IdentityDbContext>();
        var passwordHasher = sp.GetRequiredService<IPasswordHasher>();

        var code = tenantCodeOverride ?? $"T{Guid.NewGuid():N}".Substring(0, 8).ToUpperInvariant();
        var email = emailOverride ?? $"u-{Guid.NewGuid():N}".Substring(0, 10) + "@test.local";

        var tenant = Tenant.Create($"Tenant {code}", code);
        tenancyDb.Tenants.Add(tenant);
        await tenancyDb.SaveChangesAsync();

        var user = User.Create(tenant.Id, email, passwordHasher.HashPassword(DefaultPassword),
            "Test", "User", role);
        identityDb.Users.Add(user);
        await identityDb.SaveChangesAsync();

        return new SeededTenant(tenant.Id, tenant.Code, user.Id, email, DefaultPassword, role);
    }

    public static async Task<string> LoginAndGetTokenAsync(
        HttpClient client, string email, string password, string tenantCode)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password,
            TenantCode = tenantCode
        });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        if (body is null || string.IsNullOrEmpty(body.Token))
            throw new InvalidOperationException("Login returned no token");
        return body.Token;
    }

    public static async Task<HttpClient> AuthenticatedClientAsync(
        AlgreenWebApplicationFactory factory, SeededTenant t)
    {
        var client = factory.CreateClient();
        var token = await LoginAndGetTokenAsync(client, t.Email, t.Password, t.TenantCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<(SeededTenant tenantA, SeededTenant tenantB)> SeedTwoTenantsAsync(
        AlgreenWebApplicationFactory factory,
        UserRole roleForA = UserRole.Admin,
        UserRole roleForB = UserRole.Admin)
    {
        var a = await SeedTenantWithUserAsync(factory, roleForA);
        var b = await SeedTenantWithUserAsync(factory, roleForB);
        return (a, b);
    }

    public static async Task<Guid> SeedAdditionalUserAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        UserRole role = UserRole.Admin)
    {
        var (id, _, _) = await SeedAdditionalUserWithCredsAsync(factory, tenantId, role);
        return id;
    }

    public static async Task<(Guid UserId, string Email, string Password)> SeedAdditionalUserWithCredsAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        UserRole role = UserRole.Admin)
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var identityDb = sp.GetRequiredService<IdentityDbContext>();
        var passwordHasher = sp.GetRequiredService<IPasswordHasher>();

        var email = $"u-{Guid.NewGuid():N}".Substring(0, 10) + "@test.local";
        var user = User.Create(tenantId, email, passwordHasher.HashPassword(DefaultPassword),
            "Test", "User", role);
        identityDb.Users.Add(user);
        await identityDb.SaveChangesAsync();
        return (user.Id, email, DefaultPassword);
    }

    public static async Task<Guid> SeedOrderAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid createdByUserId,
        string? orderNumber = null)
    {
        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var number = orderNumber ?? $"ORD-{Guid.NewGuid():N}".Substring(0, 16);
        var order = Order.Create(
            tenantId,
            number,
            DateTime.UtcNow.AddDays(7),
            priority: 3,
            OrderType.Standard,
            createdByUserId,
            notes: null);

        ordersDb.Orders.Add(order);
        await ordersDb.SaveChangesAsync();
        return order.Id;
    }

    public static async Task<Guid> SeedProcessAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid? createdByUserId = null,
        string? processName = null)
    {
        using var scope = factory.Services.CreateScope();
        var productionDb = scope.ServiceProvider.GetRequiredService<ProductionDbContext>();

        var name = processName ?? $"Proc-{Guid.NewGuid():N}".Substring(0, 12);
        var code = $"P{Guid.NewGuid():N}".Substring(0, 6).ToUpperInvariant();
        var process = Process.Create(tenantId, code, name, sequenceOrder: 1, createdByUserId);

        productionDb.Processes.Add(process);
        await productionDb.SaveChangesAsync();
        return process.Id;
    }

    public static async Task<Guid> SeedNotificationAsync(
        AlgreenWebApplicationFactory factory,
        Guid tenantId,
        Guid userId,
        string? title = null)
    {
        using var scope = factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        var notification = Notification.Create(
            tenantId,
            userId,
            NotificationType.DeadlineWarning,
            title ?? $"Notif-{Guid.NewGuid():N}".Substring(0, 12),
            message: "Cross-tenant test notification");

        ordersDb.Notifications.Add(notification);
        await ordersDb.SaveChangesAsync();
        return notification.Id;
    }

    private sealed record LoginResponse(string Token, string RefreshToken);
}

public sealed record SeededTenant(
    Guid TenantId,
    string TenantCode,
    Guid UserId,
    string Email,
    string Password,
    UserRole Role);
