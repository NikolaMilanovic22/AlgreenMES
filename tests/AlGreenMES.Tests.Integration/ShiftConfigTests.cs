using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Shift CRUD with the 4 new time-tracking fields (Bojan spec 25.05.2026):
/// BreakMinutes, MaxOvertimeHours, AutoLogoutAfterHours, AlarmBeforeLogoutMinutes.
/// </summary>
public class ShiftConfigTests : IntegrationTestBase
{
    public ShiftConfigTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task CreateShift_persists_all_four_config_fields()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/shifts", new
        {
            Name = "Test Shift",
            StartTime = "06:00:00",
            EndTime = "14:00:00",
            BreakMinutes = 30,
            MaxOvertimeHours = 4,
            AutoLogoutAfterHours = 3,
            AlarmBeforeLogoutMinutes = 10,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var shiftId = doc.RootElement.GetProperty("id").GetGuid();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var shift = await db.Shifts.IgnoreQueryFilters().SingleAsync(s => s.Id == shiftId);
        shift.BreakMinutes.Should().Be(30);
        shift.MaxOvertimeHours.Should().Be(4);
        shift.AutoLogoutAfterHours.Should().Be(3);
        shift.AlarmBeforeLogoutMinutes.Should().Be(10);
    }

    [Fact]
    public async Task UpdateShift_persists_all_four_config_fields()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var shiftId = await TestDataSeeder.SeedShiftAsync(Factory, t.TenantId);

        var resp = await client.PutAsJsonAsync($"/api/shifts/{shiftId}", new
        {
            Name = "Updated",
            StartTime = "07:00:00",
            EndTime = "15:00:00",
            IsActive = true,
            BreakMinutes = 45,
            MaxOvertimeHours = 8,
            AutoLogoutAfterHours = 4,
            AlarmBeforeLogoutMinutes = 15,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var shift = await db.Shifts.IgnoreQueryFilters().SingleAsync(s => s.Id == shiftId);
        shift.BreakMinutes.Should().Be(45);
        shift.MaxOvertimeHours.Should().Be(8);
        shift.AutoLogoutAfterHours.Should().Be(4);
        shift.AlarmBeforeLogoutMinutes.Should().Be(15);
    }

    [Fact]
    public async Task GetShifts_returns_new_fields_in_response()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        await TestDataSeeder.SeedShiftAsync(
            Factory, t.TenantId,
            breakMinutes: 20,
            maxOvertimeHours: 5,
            autoLogoutAfterHours: 3,
            alarmBeforeLogoutMinutes: 7);

        var resp = await client.GetAsync("/api/shifts");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var first = doc.RootElement.GetProperty("items").EnumerateArray().First();
        first.GetProperty("breakMinutes").GetInt32().Should().Be(20);
        first.GetProperty("maxOvertimeHours").GetInt32().Should().Be(5);
        first.GetProperty("autoLogoutAfterHours").GetInt32().Should().Be(3);
        first.GetProperty("alarmBeforeLogoutMinutes").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task CreateShift_blocks_Department_user_with_403()
    {
        // Per ShiftsController: [Authorize(Roles="SuperAdmin,Admin,Manager")].
        // Department (factory floor worker) must NOT be able to create shifts.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/shifts", new
        {
            Name = "Sneaky", StartTime = "06:00:00", EndTime = "14:00:00",
            BreakMinutes = 0, MaxOvertimeHours = 6, AutoLogoutAfterHours = 2,
            AlarmBeforeLogoutMinutes = 5,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateShift_blocks_Department_user_with_403()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var shiftId = await TestDataSeeder.SeedShiftAsync(Factory, t.TenantId);

        var resp = await client.PutAsJsonAsync($"/api/shifts/{shiftId}", new
        {
            Name = "Sneaky", StartTime = "06:00:00", EndTime = "14:00:00",
            IsActive = true, BreakMinutes = 0, MaxOvertimeHours = 6,
            AutoLogoutAfterHours = 2, AlarmBeforeLogoutMinutes = 5,
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateShift_blocks_cross_tenant_writes()
    {
        // Tenant A creates a shift; tenant B's Admin must NOT be able to flip
        // its config — that would leak across the tenant boundary.
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        var shiftA = await TestDataSeeder.SeedShiftAsync(Factory, a.TenantId, breakMinutes: 0);
        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, b);

        var resp = await clientB.PutAsJsonAsync($"/api/shifts/{shiftA}", new
        {
            Name = "Hijack", StartTime = "06:00:00", EndTime = "14:00:00",
            IsActive = true, BreakMinutes = 999, MaxOvertimeHours = 999,
            AutoLogoutAfterHours = 999, AlarmBeforeLogoutMinutes = 999,
        });
        // Tenant filter hides A's shift from B → 404/403; either way, the
        // important property is that A's row is NOT mutated.
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var shift = await db.Shifts.IgnoreQueryFilters().SingleAsync(s => s.Id == shiftA);
        shift.BreakMinutes.Should().Be(0);
        shift.MaxOvertimeHours.Should().Be(6); // unchanged seed default
    }

    [Fact]
    public async Task GetShifts_isolates_data_across_tenants()
    {
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory);
        await TestDataSeeder.SeedShiftAsync(Factory, a.TenantId, name: "A-Shift");

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, b);
        var resp = await clientB.GetAsync("/api/shifts");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotContain(s => s.GetProperty("name").GetString() == "A-Shift");
    }

    [Fact]
    public async Task CreateShift_rejects_negative_config_values()
    {
        // Domain validator throws DomainException for negative values; the
        // global exception handler maps that to a 4xx response.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsJsonAsync("/api/shifts", new
        {
            Name = "Bad",
            StartTime = "06:00:00",
            EndTime = "14:00:00",
            BreakMinutes = -1,
            MaxOvertimeHours = 6,
            AutoLogoutAfterHours = 2,
            AlarmBeforeLogoutMinutes = 5,
        });
        // The middleware maps DomainException → 400 BadRequest (typical).
        // Either way, the request must NOT succeed.
        resp.IsSuccessStatusCode.Should().BeFalse();
    }
}
