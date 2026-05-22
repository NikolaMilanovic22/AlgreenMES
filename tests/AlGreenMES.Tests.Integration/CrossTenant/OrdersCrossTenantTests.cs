using System.Net;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration.CrossTenant;

public class OrdersCrossTenantTests : IntegrationTestBase
{
    public OrdersCrossTenantTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetOrderById_FromOtherTenant_Returns404()
    {
        var (tenantA, tenantB) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var orderInB = await TestDataSeeder.SeedOrderAsync(Factory, tenantB.TenantId, tenantB.UserId);
        var clientA = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantA);

        var response = await clientA.GetAsync($"/api/orders/{orderInB}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "tenant A must not be able to see tenant B's orders by ID");
    }

    [Fact]
    public async Task GetOrders_DoesNotReturnOtherTenantOrders()
    {
        // Sprint 2.4b removed the ?tenantId= query param; tenant comes from JWT.
        // Even if a stray ?tenantId=B is appended, the server ignores it (param is gone),
        // so the asserted invariant is unchanged: list only returns the JWT tenant's orders.
        var (tenantA, tenantB) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        await TestDataSeeder.SeedOrderAsync(Factory, tenantA.TenantId, tenantA.UserId, orderNumber: "A-MARKER-ORDER");
        await TestDataSeeder.SeedOrderAsync(Factory, tenantB.TenantId, tenantB.UserId, orderNumber: "B-MARKER-ORDER");
        var clientA = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantA);

        var response = await clientA.GetAsync($"/api/orders?tenantId={tenantB.TenantId}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("B-MARKER-ORDER",
            "tenant A must not see tenant B's orders even if a foreign tenantId is appended to the query");
    }

    [Fact]
    public async Task CancelOrder_FromOtherTenant_Returns404_AndDoesNotMutate()
    {
        var (tenantA, tenantB) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var orderInB = await TestDataSeeder.SeedOrderAsync(Factory, tenantB.TenantId, tenantB.UserId);
        var clientA = await TestDataSeeder.AuthenticatedClientAsync(Factory, tenantA);

        var response = await clientA.PostAsync($"/api/orders/{orderInB}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "tenant A must not be able to cancel tenant B's orders");

        using var scope = Factory.Services.CreateScope();
        var ordersDb = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        // Test verification runs outside an HTTP context; bypass the new tenant query filter.
        var order = await ordersDb.Orders.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderInB);
        order.Should().NotBeNull("tenant B's order must still exist");
        order!.Status.Should().NotBe(OrderStatus.Cancelled,
            "tenant A's cancel attempt must not have mutated tenant B's order");
    }
}
