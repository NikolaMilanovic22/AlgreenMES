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
    private record StockHistoryRowResp(
        Guid Id, Guid MaterialId, string MaterialCode, string MaterialName, string Unit,
        string Category, decimal? DimensionX, decimal? DimensionY, decimal? DimensionZ,
        string Type, decimal Quantity, decimal UnitPrice, decimal TotalPrice,
        DateTime MovementDate, string DocumentReference, string? Notes, DateTime CreatedAt,
        Guid? ProcessId, string? ProcessName);
    private record HistoryPageResp(IReadOnlyList<StockHistoryRowResp> Items, int TotalCount);
    private record NotificationRowResp(Guid Id, string Type, string Title, string Message, string? ReferenceType, Guid? ReferenceId, bool IsRead, DateTime CreatedAt, string? ParamsJson);
    private record NotificationPageResp(IReadOnlyList<NotificationRowResp> Items, int TotalCount);
    private record ProcessResp(Guid Id, string Code, string Name);
    private record ImportErrorResp(int RowIndex, string Code, string Reason);
    private record ImportResultResp(int Created, IReadOnlyList<ImportErrorResp> Errors);

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

    // ─── Saša 09.06.2026 batch ────────────────────────────────────────

    [Fact]
    public async Task Outflow_that_would_take_stock_below_zero_is_rejected()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var materialId = await SeedMaterialAsync(client, "M-NEG", min: 0, max: 100);

        // Put 2 on stock.
        await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/1", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 2m, unitPrice = (decimal?)10m, notes = (string?)null } }
        });

        // Try to take 100.
        var resp = await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-X", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 100m, unitPrice = (decimal?)null, notes = (string?)null } }
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("STOCK_INSUFFICIENT");
        body.Should().Contain("Nedovoljno na stanju");

        // Stock is unchanged.
        var stanje = (await (await client.GetAsync("/api/warehouse/stock")).Content.ReadFromJsonAsync<List<StanjeResp>>())!;
        stanje.Single(s => s.MaterialId == materialId).Quantity.Should().Be(2m);
    }

    [Fact]
    public async Task Outflow_multiline_same_material_over_stock_is_rejected_atomically()
    {
        // CreateStockEntryCommandHandler groups Outflow lines by material and
        // sums them BEFORE any save (~51-72). Stock 5; a single Outflow with two
        // 3-unit lines of the SAME material totals 6 > 5 → STOCK_INSUFFICIENT.
        // Nothing must persist: stock stays 5 and no history rows are written.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var materialId = await SeedMaterialAsync(client, "M-ATOM", min: 0, max: 100);
        await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/A", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 5m, unitPrice = (decimal?)10m, notes = (string?)null } }
        });

        var resp = await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-ATOM", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[]
            {
                new { materialId, quantity = 3m, unitPrice = (decimal?)null, notes = (string?)null },
                new { materialId, quantity = 3m, unitPrice = (decimal?)null, notes = (string?)null },
            }
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("STOCK_INSUFFICIENT");

        // Stock unchanged (still the 5 from the Inflow).
        var stanje = (await (await client.GetAsync("/api/warehouse/stock")).Content.ReadFromJsonAsync<List<StanjeResp>>())!;
        stanje.Single(s => s.MaterialId == materialId).Quantity.Should().Be(5m);

        // Zero Outflow history rows for this material — the reject was atomic.
        var page = (await (await client.GetAsync($"/api/warehouse/history?materialId={materialId}&type=Outflow")).Content.ReadFromJsonAsync<HistoryPageResp>())!;
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task MaterialLowStock_does_not_fire_when_outflow_lands_exactly_on_min()
    {
        // Boundary (CreateStockEntryCommandHandler ~131): the notification fires
        // only when before >= min AND after < min. min=5, stock 10, take 5 →
        // after = 5, which is NOT below min → silent. Take 1 more → after = 4 <
        // min → exactly one notification.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var adminClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var (_, coordEmail, coordPw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId, UserRole.Coordinator);
        var coordClient = Factory.CreateClient();
        coordClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", await TestDataSeeder.LoginAndGetTokenAsync(coordClient, coordEmail, coordPw, t.TenantCode));

        var materialId = await SeedMaterialAsync(adminClient, "M-EXACT", min: 5, max: 100);
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/E", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 10m, unitPrice = (decimal?)5m, notes = (string?)null } }
        });

        // Take 5 → on-hand exactly 5 (== min, not below) → NO notification.
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-E1", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 5m, unitPrice = (decimal?)null, notes = (string?)null } }
        });

        async Task<int> LowCountAsync()
        {
            var p = (await (await coordClient.GetAsync("/api/notifications?pageSize=50")).Content.ReadFromJsonAsync<NotificationPageResp>())!;
            return p.Items.Count(n => n.Type == "MaterialLowStock" && n.ReferenceId == materialId);
        }

        (await LowCountAsync()).Should().Be(0, "on-hand landed exactly on min, not below it");

        // Take 1 more → on-hand 4 < min → exactly one notification.
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-E2", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 1m, unitPrice = (decimal?)null, notes = (string?)null } }
        });

        (await LowCountAsync()).Should().Be(1, "crossing from at-min to below-min fires exactly once");
    }

    [Fact]
    public async Task Material_Update_with_min_greater_than_max_is_rejected()
    {
        // Material.ValidateThresholds (~112-118): min > max (when max > 0) →
        // MATERIAL_MIN_GT_MAX; negative min → MATERIAL_MIN_NEGATIVE. Domain
        // errors surface as 400.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var materialId = await SeedMaterialAsync(client, "M-THRESH", min: 5, max: 50);

        var minGtMax = await client.PutAsJsonAsync($"/api/materials/{materialId}", new
        {
            name = "Test", unit = "kom", category = "Test",
            minQuantity = 100, maxQuantity = 10,
            dimensionX = (decimal?)null, dimensionY = (decimal?)null, dimensionZ = (decimal?)null,
            location = (string?)null, notes = (string?)null,
        });
        minGtMax.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
        (await minGtMax.Content.ReadAsStringAsync()).Should().Contain("MATERIAL_MIN_GT_MAX");

        var negativeMin = await client.PutAsJsonAsync($"/api/materials/{materialId}", new
        {
            name = "Test", unit = "kom", category = "Test",
            minQuantity = -1, maxQuantity = 50,
            dimensionX = (decimal?)null, dimensionY = (decimal?)null, dimensionZ = (decimal?)null,
            location = (string?)null, notes = (string?)null,
        });
        negativeMin.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
        (await negativeMin.Content.ReadAsStringAsync()).Should().Contain("MATERIAL_MIN_NEGATIVE");
    }

    [Fact]
    public async Task History_returns_MaterialCode_and_MaterialName_in_English_DTO_fields()
    {
        // Regression guard: StockMovementDto.MaterialCode / MaterialName used
        // to be MaterialKod / MaterialNaziv and serialized to camelCase Serbian
        // keys, which the FE never picked up. Don't let that regress.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var materialId = await SeedMaterialAsync(client, "M-EN", "Profil");
        await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/EN", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 1m, unitPrice = (decimal?)1m, notes = (string?)null } }
        });

        var page = (await (await client.GetAsync("/api/warehouse/history")).Content.ReadFromJsonAsync<HistoryPageResp>())!;
        var row = page.Items.Single(r => r.MaterialId == materialId);
        row.MaterialCode.Should().Be("M-EN");
        row.MaterialName.Should().Be("Profil");
    }

    [Fact]
    public async Task History_filters_by_category()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var mProfile = await SeedMaterialAsync(client, "M-PROFILE");
        var mGlass = await SeedMaterialAsync(client, "M-GLASS");
        // Two materials, both in the same seeded category "Test" → swap one's category.
        await client.PutAsJsonAsync($"/api/materials/{mGlass}", new
        {
            name = "Staklo", unit = "kom", category = "Staklo",
            minQuantity = 0, maxQuantity = 0,
            dimensionX = (decimal?)null, dimensionY = (decimal?)null, dimensionZ = (decimal?)null,
            location = (string?)null, notes = (string?)null,
        });

        await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/CAT", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[]
            {
                new { materialId = mProfile, quantity = 1m, unitPrice = (decimal?)1m, notes = (string?)null },
                new { materialId = mGlass, quantity = 1m, unitPrice = (decimal?)1m, notes = (string?)null },
            }
        });

        var page = (await (await client.GetAsync("/api/warehouse/history?category=Staklo")).Content.ReadFromJsonAsync<HistoryPageResp>())!;
        page.Items.Should().OnlyContain(r => r.MaterialId == mGlass);
        page.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task History_accepts_sortBy_quantity_ascending()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var materialId = await SeedMaterialAsync(client, "M-SORT", min: 0, max: 1000);
        await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/Q", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[]
            {
                new { materialId, quantity = 7m, unitPrice = (decimal?)1m, notes = (string?)null },
                new { materialId, quantity = 1m, unitPrice = (decimal?)1m, notes = (string?)null },
                new { materialId, quantity = 3m, unitPrice = (decimal?)1m, notes = (string?)null },
            }
        });

        var asc = (await (await client.GetAsync("/api/warehouse/history?materialId=" + materialId + "&sortBy=quantity&sortDirection=asc")).Content.ReadFromJsonAsync<HistoryPageResp>())!;
        asc.Items.Select(i => i.Quantity).Should().ContainInOrder(1m, 3m, 7m);

        var desc = (await (await client.GetAsync("/api/warehouse/history?materialId=" + materialId + "&sortBy=quantity&sortDirection=desc")).Content.ReadFromJsonAsync<HistoryPageResp>())!;
        desc.Items.Select(i => i.Quantity).Should().ContainInOrder(7m, 3m, 1m);
    }

    [Fact]
    public async Task Outflow_persists_optional_ProcessId_and_history_returns_ProcessName()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, processName: "Krojenje-A");

        var materialId = await SeedMaterialAsync(client, "M-PROC", min: 0, max: 100);
        await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/P", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 10m, unitPrice = (decimal?)5m, notes = (string?)null } }
        });

        var izlaz = await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-P", movementDate = DateTime.UtcNow, notes = (string?)null,
            processId,
            lines = new[] { new { materialId, quantity = 4m, unitPrice = (decimal?)null, notes = (string?)null } }
        });
        izlaz.IsSuccessStatusCode.Should().BeTrue();

        var page = (await (await client.GetAsync("/api/warehouse/history?type=Outflow")).Content.ReadFromJsonAsync<HistoryPageResp>())!;
        var row = page.Items.Single(r => r.MaterialId == materialId);
        row.ProcessId.Should().Be(processId);
        row.ProcessName.Should().Contain("Krojenje-A"); // formatted as "Code — Name", seeded name is "Krojenje-A"
    }

    [Fact]
    public async Task Inflow_silently_drops_ProcessId_even_if_caller_sent_one()
    {
        // Domain invariant: Inflow never stores a ProcessId. If a misbehaving
        // client sends one, the entity throws it away.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, processName: "Krojenje-B");

        var materialId = await SeedMaterialAsync(client, "M-DROP", min: 0, max: 100);
        await client.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/D", movementDate = DateTime.UtcNow, notes = (string?)null,
            processId,
            lines = new[] { new { materialId, quantity = 5m, unitPrice = (decimal?)1m, notes = (string?)null } }
        });

        var page = (await (await client.GetAsync("/api/warehouse/history?type=Inflow")).Content.ReadFromJsonAsync<HistoryPageResp>())!;
        var row = page.Items.Single(r => r.MaterialId == materialId);
        row.ProcessId.Should().BeNull();
        row.ProcessName.Should().BeNull();
    }

    [Fact]
    public async Task MaterialLowStock_notification_is_created_when_outflow_crosses_min()
    {
        // Seed a tenant + an Admin (issuer) + a Coordinator (recipient).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var adminClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var (coordId, coordEmail, coordPw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId, UserRole.Coordinator);
        var coordClient = Factory.CreateClient();
        var coordToken = await TestDataSeeder.LoginAndGetTokenAsync(coordClient, coordEmail, coordPw, t.TenantCode);
        coordClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", coordToken);

        var materialId = await SeedMaterialAsync(adminClient, "M-LOW", min: 5, max: 100);
        // Put 10 on stock.
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/L", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 10m, unitPrice = (decimal?)5m, notes = (string?)null } }
        });

        // Take 7 → on-hand 3 → crosses below min=5.
        var izlaz = await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-L", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 7m, unitPrice = (decimal?)null, notes = (string?)null } }
        });
        izlaz.IsSuccessStatusCode.Should().BeTrue();

        // Coordinator should now see a MaterialLowStock notification.
        var notif = (await (await coordClient.GetAsync("/api/notifications")).Content.ReadFromJsonAsync<NotificationPageResp>())!;
        var low = notif.Items.FirstOrDefault(n => n.Type == "MaterialLowStock");
        low.Should().NotBeNull("crossing-below-min should emit a notification");
        low!.ReferenceType.Should().Be("Material");
        low.ReferenceId.Should().Be(materialId);
        low.ParamsJson.Should().NotBeNull();
        // jsonb storage normalises spacing; parse instead of substring-matching.
        using var doc = System.Text.Json.JsonDocument.Parse(low.ParamsJson!);
        doc.RootElement.GetProperty("code").GetString().Should().Be("M-LOW");
        doc.RootElement.GetProperty("min").GetInt32().Should().Be(5);
        doc.RootElement.GetProperty("onHand").GetDecimal().Should().Be(3m);
        doc.RootElement.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        doc.RootElement.GetProperty("unit").GetString().Should().Be("kom");
    }

    [Fact]
    public async Task MaterialLowStock_is_NOT_created_when_material_was_already_below_min()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var adminClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var (_, coordEmail, coordPw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId, UserRole.Coordinator);
        var coordClient = Factory.CreateClient();
        var coordToken = await TestDataSeeder.LoginAndGetTokenAsync(coordClient, coordEmail, coordPw, t.TenantCode);
        coordClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", coordToken);

        var materialId = await SeedMaterialAsync(adminClient, "M-LOW2", min: 5, max: 100);
        // Put 10, take 7 — first crossing fires one notification.
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/L2", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 10m, unitPrice = (decimal?)5m, notes = (string?)null } }
        });
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-L2A", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 7m, unitPrice = (decimal?)null, notes = (string?)null } }
        });
        // Take 2 more — already below min, no second notification.
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-L2B", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 2m, unitPrice = (decimal?)null, notes = (string?)null } }
        });

        var notif = (await (await coordClient.GetAsync("/api/notifications?pageSize=50")).Content.ReadFromJsonAsync<NotificationPageResp>())!;
        var lowForThisMaterial = notif.Items
            .Where(n => n.Type == "MaterialLowStock" && n.ReferenceId == materialId)
            .ToList();
        lowForThisMaterial.Should().HaveCount(1, "the second Izlaz did not cross — it took from an already-below-min position");
    }

    [Fact]
    public async Task MaterialLowStock_fires_again_after_reset_above_min_then_crossing_back()
    {
        // Crossing #1 → 1 notification. Refill above min. Crossing #2 → must
        // fire a second notification. Anti-spam should only suppress when the
        // material is already below min — not after a genuine reset.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var adminClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var (_, coordEmail, coordPw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId, UserRole.Coordinator);
        var coordClient = Factory.CreateClient();
        var coordToken = await TestDataSeeder.LoginAndGetTokenAsync(coordClient, coordEmail, coordPw, t.TenantCode);
        coordClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", coordToken);

        var materialId = await SeedMaterialAsync(adminClient, "M-RESET", min: 5, max: 100);

        // Cycle 1: in 10, out 7 → 3 (below min) → notification #1
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/R1", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 10m, unitPrice = (decimal?)5m, notes = (string?)null } }
        });
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-R1", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 7m, unitPrice = (decimal?)null, notes = (string?)null } }
        });
        // Cycle 2: in 10 → 13 (above min, no notification), out 9 → 4 (below min) → notification #2
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/R2", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 10m, unitPrice = (decimal?)5m, notes = (string?)null } }
        });
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-R2", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 9m, unitPrice = (decimal?)null, notes = (string?)null } }
        });

        var notif = (await (await coordClient.GetAsync("/api/notifications?pageSize=50")).Content.ReadFromJsonAsync<NotificationPageResp>())!;
        notif.Items
            .Where(n => n.Type == "MaterialLowStock" && n.ReferenceId == materialId)
            .Should().HaveCount(2, "each genuine crossing below min must emit a notification");
    }

    [Fact]
    public async Task MaterialLowStock_fans_out_to_every_management_user_in_the_tenant()
    {
        // Seed Admin (issuer) + a Manager + a SuperAdmin + (Coordinator is the
        // primary seeded with the tenant). All four are "management" per
        // NotificationCreator.ManagementRoles and must receive the notification.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Coordinator);
        var coordClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var (_, adminEmail, adminPw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId, UserRole.Admin);
        var adminClient = Factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", await TestDataSeeder.LoginAndGetTokenAsync(adminClient, adminEmail, adminPw, t.TenantCode));

        var (_, managerEmail, managerPw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId, UserRole.Manager);
        var managerClient = Factory.CreateClient();
        managerClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", await TestDataSeeder.LoginAndGetTokenAsync(managerClient, managerEmail, managerPw, t.TenantCode));

        var (_, superEmail, superPw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId, UserRole.SuperAdmin);
        var superClient = Factory.CreateClient();
        superClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", await TestDataSeeder.LoginAndGetTokenAsync(superClient, superEmail, superPw, t.TenantCode));

        // Admin issues the Inflow + Outflow (Coordinator can't POST entries).
        var materialId = await SeedMaterialAsync(adminClient, "M-FANOUT", min: 5, max: 100);
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/F", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 10m, unitPrice = (decimal?)5m, notes = (string?)null } }
        });
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-F", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 7m, unitPrice = (decimal?)null, notes = (string?)null } }
        });

        async Task<int> CountForAsync(HttpClient c)
        {
            var p = (await (await c.GetAsync("/api/notifications?pageSize=50")).Content.ReadFromJsonAsync<NotificationPageResp>())!;
            return p.Items.Count(n => n.Type == "MaterialLowStock" && n.ReferenceId == materialId);
        }

        (await CountForAsync(coordClient)).Should().Be(1, "Coordinator is management");
        (await CountForAsync(adminClient)).Should().Be(1, "Admin is management (issuer also receives)");
        (await CountForAsync(managerClient)).Should().Be(1, "Manager is management");
        (await CountForAsync(superClient)).Should().Be(1, "SuperAdmin is management");
    }

    [Fact]
    public async Task MaterialLowStock_does_NOT_reach_SalesManager_or_Department()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var adminClient = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var (_, salesEmail, salesPw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId, UserRole.SalesManager);
        var salesClient = Factory.CreateClient();
        salesClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", await TestDataSeeder.LoginAndGetTokenAsync(salesClient, salesEmail, salesPw, t.TenantCode));

        var (_, deptEmail, deptPw) = await TestDataSeeder.SeedAdditionalUserWithCredsAsync(Factory, t.TenantId, UserRole.Department);
        var deptClient = Factory.CreateClient();
        deptClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", await TestDataSeeder.LoginAndGetTokenAsync(deptClient, deptEmail, deptPw, t.TenantCode));

        var materialId = await SeedMaterialAsync(adminClient, "M-NOFAN", min: 5, max: 100);
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Inflow", documentReference = "U/N", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 10m, unitPrice = (decimal?)5m, notes = (string?)null } }
        });
        await adminClient.PostAsJsonAsync("/api/warehouse/entries", new
        {
            type = "Outflow", documentReference = "ORD-N", movementDate = DateTime.UtcNow, notes = (string?)null,
            lines = new[] { new { materialId, quantity = 7m, unitPrice = (decimal?)null, notes = (string?)null } }
        });

        var salesNotif = (await (await salesClient.GetAsync("/api/notifications?pageSize=50")).Content.ReadFromJsonAsync<NotificationPageResp>())!;
        salesNotif.Items.Should().NotContain(n => n.Type == "MaterialLowStock" && n.ReferenceId == materialId,
            "SalesManager is not in the management role set");

        var deptNotif = (await (await deptClient.GetAsync("/api/notifications?pageSize=50")).Content.ReadFromJsonAsync<NotificationPageResp>())!;
        deptNotif.Items.Should().NotContain(n => n.Type == "MaterialLowStock" && n.ReferenceId == materialId,
            "Department worker is not in the management role set");
    }

    [Fact]
    public async Task Materials_import_creates_valid_rows_and_reports_errors()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // Seed one existing material so we can verify the DB-duplicate path.
        await SeedMaterialAsync(client, "M-EXISTS");

        var resp = await client.PostAsJsonAsync("/api/materials/import", new
        {
            items = new object[]
            {
                new { code = "I-1", name = "Imported 1", unit = "kom", category = "Profil", minQuantity = 0, maxQuantity = 10, dimensionX = (decimal?)null, dimensionY = (decimal?)null, dimensionZ = (decimal?)null, location = (string?)null, notes = (string?)null },
                new { code = "I-2", name = "Imported 2", unit = "kom", category = "Profil", minQuantity = 0, maxQuantity = 10, dimensionX = (decimal?)null, dimensionY = (decimal?)null, dimensionZ = (decimal?)null, location = (string?)null, notes = (string?)null },
                new { code = "I-1", name = "DUP IN BATCH", unit = "kom", category = "Profil", minQuantity = 0, maxQuantity = 10, dimensionX = (decimal?)null, dimensionY = (decimal?)null, dimensionZ = (decimal?)null, location = (string?)null, notes = (string?)null },
                new { code = "M-EXISTS", name = "DUP IN DB", unit = "kom", category = "Profil", minQuantity = 0, maxQuantity = 10, dimensionX = (decimal?)null, dimensionY = (decimal?)null, dimensionZ = (decimal?)null, location = (string?)null, notes = (string?)null },
                new { code = "  ", name = "EMPTY CODE", unit = "kom", category = "Profil", minQuantity = 0, maxQuantity = 10, dimensionX = (decimal?)null, dimensionY = (decimal?)null, dimensionZ = (decimal?)null, location = (string?)null, notes = (string?)null },
            }
        });
        resp.IsSuccessStatusCode.Should().BeTrue();
        var result = (await resp.Content.ReadFromJsonAsync<ImportResultResp>())!;
        result.Created.Should().Be(2, "I-1 and I-2 are valid; the rest fail");
        result.Errors.Should().HaveCount(3);
        result.Errors.Should().Contain(e => e.Code == "I-1" && e.Reason.Contains("Duplikat"));
        result.Errors.Should().Contain(e => e.Code == "M-EXISTS" && e.Reason.Contains("postoji"));
        result.Errors.Should().Contain(e => e.Reason.Contains("prazan"));
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
