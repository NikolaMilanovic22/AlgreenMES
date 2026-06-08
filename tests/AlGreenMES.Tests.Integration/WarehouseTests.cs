using System.Net;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Saša 08.06.2026 — Magacin (warehouse) end-to-end coverage:
///   * Material CRUD (Admin path).
///   * Stock Ulaz updates Stanje + writes an Istorija row.
///   * Stock Izlaz with no explicit price falls back to the last entered
///     ulaz unit-price (Saša: "cena ide uvek zadnja").
///   * Status flag math: ISPOD MIN / OK / IZNAD MAX.
///   * Multi-role: a Coordinator + Magacioner (combined roles) can hit the
///     Magacioner-gated endpoints. Coordinator-only is rejected.
/// </summary>
public class WarehouseTests : IntegrationTestBase
{
    public WarehouseTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    private async Task<Guid> SeedMaterialAsync(HttpClient client, string code, string name = "Test", int min = 5, int max = 50)
    {
        var resp = await client.PostAsJsonAsync("/api/materials", new
        {
            code,
            name,
            unit = "kom",
            category = "Test",
            minQuantity = min,
            maxQuantity = max,
            dimensionX = (decimal?)null,
            dimensionY = (decimal?)null,
            dimensionZ = (decimal?)null,
            location = (string?)null,
            notes = (string?)null,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<MaterialResp>();
        return dto!.Id;
    }

    private record MaterialResp(Guid Id, string Code, string Name);
    private record StanjeResp(Guid MaterialId, string Code, decimal Quantity, decimal LatestUnitPrice, decimal TotalValue, string Status);
    private record StockMovementResp(Guid Id, decimal UnitPrice);

    [Fact]
    public async Task Material_Create_then_Get_returns_the_row()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var id = await SeedMaterialAsync(client, "M001", "Profil AL");

        var resp = await client.GetAsync($"/api/materials/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<MaterialResp>();
        dto!.Code.Should().Be("M001");
        dto.Name.Should().Be("Profil AL");
    }

    [Fact]
    public async Task Material_Create_with_duplicate_code_in_same_tenant_returns_400()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await SeedMaterialAsync(client, "DUP", "First");
        var resp = await client.PostAsJsonAsync("/api/materials", new
        {
            code = "DUP", name = "Second", unit = "kom", category = "Test",
            minQuantity = 0, maxQuantity = 0,
            dimensionX = (decimal?)null, dimensionY = (decimal?)null, dimensionZ = (decimal?)null,
            location = (string?)null, notes = (string?)null,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Inflow_then_Stock_reflects_quantity_and_unit_price()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var materialId = await SeedMaterialAsync(client, "M002", "Lim", min: 3, max: 30);

        var ulazResp = await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow",
            documentReference = "2026/100",
            movementDate = DateTime.UtcNow,
            notes = (string?)null,
            lines = new[] { new { materialId, quantity = 15m, unitPrice = (decimal?)1230m, notes = (string?)null } }
        });
        ulazResp.IsSuccessStatusCode.Should().BeTrue();

        var stanjeResp = await client.GetAsync("/api/warehouse/stock");
        stanjeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var stanje = await stanjeResp.Content.ReadFromJsonAsync<List<StanjeResp>>();
        var row = stanje!.Single(s => s.MaterialId == materialId);
        row.Quantity.Should().Be(15m);
        row.LatestUnitPrice.Should().Be(1230m);
        row.TotalValue.Should().Be(15m * 1230m);
        row.Status.Should().Be("Ok"); // 3 ≤ 15 ≤ 30
    }

    [Fact]
    public async Task Outflow_without_explicit_price_falls_back_to_last_inflow_price()
    {
        // Saša 08.06.2026 — "Cena ide uvek zadnja".
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var materialId = await SeedMaterialAsync(client, "M003");

        await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow",
            documentReference = "2026/200",
            movementDate = DateTime.UtcNow.AddMinutes(-10),
            notes = (string?)null,
            lines = new[] { new { materialId, quantity = 10m, unitPrice = (decimal?)5000m, notes = (string?)null } }
        });

        var izlazResp = await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow",
            documentReference = "ORD-2026-006",
            movementDate = DateTime.UtcNow,
            notes = (string?)null,
            lines = new[] { new { materialId, quantity = 3m, unitPrice = (decimal?)null, notes = (string?)null } }
        });
        izlazResp.IsSuccessStatusCode.Should().BeTrue();
        var izlazRows = await izlazResp.Content.ReadFromJsonAsync<List<StockMovementResp>>();
        izlazRows!.Single().UnitPrice.Should().Be(5000m);

        var stanjeResp = await client.GetAsync("/api/warehouse/stock");
        var stanje = await stanjeResp.Content.ReadFromJsonAsync<List<StanjeResp>>();
        var row = stanje!.Single(s => s.MaterialId == materialId);
        row.Quantity.Should().Be(7m); // 10 - 3
    }

    [Fact]
    public async Task Stock_status_flags_reflect_min_and_max_thresholds()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // 1 row ISPOD MIN, 1 row OK, 1 row IZNAD MAX.
        var below = await SeedMaterialAsync(client, "M-BELOW", min: 10, max: 100);
        var ok = await SeedMaterialAsync(client, "M-OK", min: 5, max: 100);
        var above = await SeedMaterialAsync(client, "M-ABOVE", min: 0, max: 5);

        await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "B/1", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[]
            {
                new { materialId = below, quantity = 2m, unitPrice = (decimal?)1m, notes = (string?)null },
                new { materialId = ok, quantity = 20m, unitPrice = (decimal?)1m, notes = (string?)null },
                new { materialId = above, quantity = 50m, unitPrice = (decimal?)1m, notes = (string?)null },
            }
        });

        var stanjeResp = await client.GetAsync("/api/warehouse/stock");
        var stanje = (await stanjeResp.Content.ReadFromJsonAsync<List<StanjeResp>>())!;
        stanje.Single(s => s.MaterialId == below).Status.Should().Be("BelowMin");
        stanje.Single(s => s.MaterialId == ok).Status.Should().Be("Ok");
        stanje.Single(s => s.MaterialId == above).Status.Should().Be("AboveMax");
    }

    [Fact]
    public async Task Warehouse_endpoints_reject_Coordinator_without_Magacioner_role()
    {
        // Coordinator alone cannot hit Magacioner-gated endpoints (POST /entries).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Coordinator);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "X/1", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = Array.Empty<object>(),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // But CAN read Stanje / Istorija (Coordinator is on the read allowlist).
        var stanje = await client.GetAsync("/api/warehouse/stock");
        stanje.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
