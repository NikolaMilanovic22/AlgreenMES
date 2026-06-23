using System.Net;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
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

    /// <summary>
    /// Saša bug, 20.06.2026: the handler now validates the OrderType
    /// code exists in the per-tenant table (replacing the old C# enum
    /// IsInEnum check). Sending a code that doesn't exist for the
    /// tenant should be rejected with INVALID_ORDER_TYPE instead of
    /// being silently stored as garbage. The positive case (custom code
    /// like "Novi" being accepted) is exercised end-to-end via the
    /// Playwright golden path + manual smoke against staging.
    /// </summary>
    [Fact]
    public async Task CreateOrder_WithUnknownOrderTypeCode_Returns400()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var orderNumber = $"TST-{Guid.NewGuid():N}".Substring(0, 16);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(orderNumber), "OrderNumber" },
            { new StringContent(DateTime.UtcNow.AddDays(7).ToString("O")), "DeliveryDate" },
            { new StringContent("3"), "Priority" },
            { new StringContent("DOES_NOT_EXIST"), "OrderType" },
        };

        var resp = await client.PostAsync("/api/orders", form);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("INVALID_ORDER_TYPE");
    }

    // Saša bug 23.06.2026: orders with a custom (admin-created) OrderType
    // code couldn't be activated — FE got "narudžbina nije nađena" after
    // POST /activate. Root cause: OrderDto/OrderDetailDto still typed
    // OrderType as the C# enum, so Mapster silently coerced unknown codes
    // on the GET-back, breaking the FE refetch. These three tests cover
    // the full path that was uncovered: create → GET → activate → GET.

    [Fact]
    public async Task CreateOrder_WithCustomOrderTypeCode_RoundtripsCodeUnchanged()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        await TestDataSeeder.SeedOrderTypeAsync(Factory, t.TenantId, "NOVI", "Novi");
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var orderNumber = $"TST-{Guid.NewGuid():N}".Substring(0, 16);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(orderNumber), "OrderNumber" },
            { new StringContent(DateTime.UtcNow.AddDays(7).ToString("O")), "DeliveryDate" },
            { new StringContent("3"), "Priority" },
            { new StringContent("NOVI"), "OrderType" },
        };

        var createResp = await client.PostAsync("/api/orders", form);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<OrderResponseDto>();
        created.Should().NotBeNull();
        created!.OrderType.Should().Be("NOVI", "the create response must preserve the custom code, not coerce to Standard");

        var getResp = await client.GetAsync($"/api/orders/{created.Id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<OrderResponseDto>();
        fetched.Should().NotBeNull();
        fetched!.OrderType.Should().Be("NOVI", "GET must echo back the same custom code that was persisted");
    }

    [Fact]
    public async Task ActivateOrder_WithCustomOrderTypeCode_Succeeds()
    {
        // Saša bug 23.06.2026 reproduction: activate-then-refetch on an
        // order with a custom OrderType code. Needs a full order (item +
        // process) so Order.Activate() passes its NO_ITEMS guard.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        await TestDataSeeder.SeedOrderTypeAsync(Factory, t.TenantId, "NOVI", "Novi");
        var categoryId = await TestDataSeeder.SeedProductCategoryAsync(Factory, t.TenantId, t.UserId);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        var (orderId, _, _) = await TestDataSeeder.SeedOrderWithProcessesAsync(
            Factory, t.TenantId, t.UserId, categoryId,
            processIds: new[] { processId },
            processStatuses: new[] { ProcessStatus.Pending },
            orderType: "NOVI");
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var activateResp = await client.PostAsync($"/api/orders/{orderId}/activate", content: null);
        activateResp.StatusCode.Should().Be(HttpStatusCode.NoContent, "the handler reads the order back via GetByIdWithFullDetailsAsync — that lookup must work for custom-code orders");

        var getResp = await client.GetAsync($"/api/orders/{orderId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<OrderResponseDto>();
        fetched.Should().NotBeNull();
        fetched!.OrderType.Should().Be("NOVI");
        fetched.Status.Should().Be("Active");
    }

    [Fact]
    public async Task GetOrderById_WithCustomOrderTypeCode_DoesNotCoerceToStandard()
    {
        // Most surgical regression guard for the Mapster coercion bug:
        // seeds an order with a custom code directly (bypassing the create
        // command), then asserts the GET response preserves it verbatim.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        await TestDataSeeder.SeedOrderTypeAsync(Factory, t.TenantId, "NOVI", "Novi");
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId, orderType: "NOVI");
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.GetAsync($"/api/orders/{orderId}");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await resp.Content.ReadFromJsonAsync<OrderResponseDto>();
        fetched.Should().NotBeNull();
        fetched!.OrderType.Should().Be("NOVI");
        fetched.OrderType.Should().NotBe("Standard", "before the DTO fix, Mapster silently coerced unknown codes to Standard");
    }

    // Minimal shape for deserializing /api/orders responses in these tests.
    // Camel-case JSON (System.Text.Json default) — Newtonsoft's
    // PropertyNameCaseInsensitive isn't applied to ReadFromJsonAsync, so
    // the property names here must match the wire format exactly.
    private sealed record OrderResponseDto(Guid Id, string OrderNumber, string OrderType, string Status);
}
