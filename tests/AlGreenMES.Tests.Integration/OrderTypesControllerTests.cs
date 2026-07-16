using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// /api/order-types CRUD (Saša 20.06.2026: admins can create custom order
/// types beyond the 4 seeded defaults). Covers the duplicate-code guard, the
/// blank-code auto-generation path, and the in-use soft-delete.
/// </summary>
public class OrderTypesControllerTests : IntegrationTestBase
{
    public OrderTypesControllerTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    private sealed record OrderTypeResp(Guid Id, string Code, string Name, bool AllowsManualProcesses, bool IsActive);
    private sealed record DeleteResp(bool HardDeleted, bool Deactivated);

    [Fact]
    public async Task CreateOrderType_WithDuplicateCode_Returns400_ORDER_TYPE_CODE_EXISTS()
    {
        // The seeder creates the 4 defaults (Standard/Repair/Complaint/Rework);
        // re-POSTing "Standard" must collide.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/order-types", new
        {
            code = "Standard",
            name = "Duplicate",
            allowsManualProcesses = false,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("ORDER_TYPE_CODE_EXISTS");
    }

    [Fact]
    public async Task CreateOrderType_WithBlankCode_Returns201_WithGeneratedCode()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/order-types", new
        {
            code = (string?)null,
            name = "Hitna porudžbina",
            allowsManualProcesses = true,
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<OrderTypeResp>();
        dto!.Name.Should().Be("Hitna porudžbina");
        // Code auto-derived from the name slug (upper-cased, non-alnum → "_").
        dto.Code.Should().NotBeNullOrWhiteSpace();
        dto.Code.Should().Be("HITNA_PORUD_BINA");
    }

    [Fact]
    public async Task DeleteOrderType_InUse_SoftDeactivates_AndOrderStillResolves()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // Custom type + an order that uses it (Order.OrderType == type.Code).
        var create = await client.PostAsJsonAsync("/api/order-types", new
        {
            code = "CUSTOMX",
            name = "Custom X",
            allowsManualProcesses = false,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var typeId = (await create.Content.ReadFromJsonAsync<OrderTypeResp>())!.Id;

        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId, orderType: "CUSTOMX");

        var del = await client.DeleteAsync($"/api/order-types/{typeId}");
        del.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await del.Content.ReadFromJsonAsync<DeleteResp>();
        result!.Deactivated.Should().BeTrue("an in-use type is soft-deactivated, not hard-deleted");
        result.HardDeleted.Should().BeFalse();

        // The referencing order still resolves.
        var order = await client.GetAsync($"/api/orders/{orderId}");
        order.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
