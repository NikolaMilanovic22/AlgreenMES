using System.Net;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

public class OrdersTests : IntegrationTestBase
{
    public OrdersTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GetOrders_WithValidToken_ReturnsList()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.GetAsync("/api/orders");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOrderById_NonexistentId_Returns404()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateOrder_WithValidPayload_Returns201()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var orderNumber = $"TST-{Guid.NewGuid():N}".Substring(0, 16);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(orderNumber), "OrderNumber" },
            { new StringContent(DateTime.UtcNow.AddDays(7).ToString("O")), "DeliveryDate" },
            { new StringContent("3"), "Priority" },
            { new StringContent("Standard"), "OrderType" },
        };

        var resp = await client.PostAsync("/api/orders", form);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
