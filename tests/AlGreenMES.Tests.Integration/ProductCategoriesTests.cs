using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// DELETE /api/product-categories/{id} (ProductCategoriesController ~97-105):
/// a category referenced by an order must NOT be hard-deleted without an
/// explicit force flag — the endpoint returns hasReferences + count and leaves
/// the category active. An unreferenced category deletes cleanly (204).
/// </summary>
public class ProductCategoriesTests : IntegrationTestBase
{
    public ProductCategoriesTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    private async Task<Guid> CreateCategoryAsync(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/product-categories", new
        {
            name,
            description = (string?)null,
            defaultWarningDays = (int?)null,
            defaultCriticalDays = (int?)null,
            processes = (object?)null,
            dependencies = (object?)null,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task DeleteCategory_WithOrderReference_ReturnsHasReferences_AndStaysActive()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var categoryId = await CreateCategoryAsync(client, "Referenced Cat");
        // An order item referencing the category makes it "in use".
        var processId = await TestDataSeeder.SeedProcessAsync(Factory, t.TenantId, t.UserId);
        await TestDataSeeder.SeedOrderItemProcessAsync(
            Factory, t.TenantId, t.UserId, processId, categoryId);

        var del = await client.DeleteAsync($"/api/product-categories/{categoryId}");
        del.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await del.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("hasReferences").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("referencedOrderCount").GetInt32().Should().Be(1);
        }

        // Still active — the soft guard did not remove or deactivate it.
        var get = await client.GetAsync($"/api/product-categories/{categoryId}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
        }
    }

    [Fact]
    public async Task DeleteCategory_WithoutReferences_Returns204()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var categoryId = await CreateCategoryAsync(client, "Orphan Cat");

        var del = await client.DeleteAsync($"/api/product-categories/{categoryId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await client.GetAsync($"/api/product-categories/{categoryId}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
