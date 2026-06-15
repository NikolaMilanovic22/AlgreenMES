using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// Coverage for the tenant /me/* endpoints + the new /me/logo upload flow
/// (15.06.2026):
/// - GET /api/tenants/me returns the caller's tenant resolved from the JWT
///   (no tenant id in the URL).
/// - POST /api/tenants/me/logo accepts a PNG/JPG/SVG multipart upload as
///   Admin/SuperAdmin and persists Tenant.LogoUrl.
/// - GET /api/tenants/me/logo streams the file back to any tenant member.
/// - DELETE clears the LogoUrl and removes the file.
/// - Content-type and extension whitelists are enforced (defense in
///   depth: ext check alone is bypassable by header forgery, MIME check
///   alone by filename rename — both required).
/// - Manager / Coordinator roles cannot upload (role gate).
/// - /me/settings GET reads, PUT writes — both scoped via JWT tenant id.
/// </summary>
public class TenantLogoTests : IntegrationTestBase
{
    public TenantLogoTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    // 1x1 transparent PNG — smallest valid image to feed the validator.
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    // ──────────────────────────────────────────────────────────────────────
    // GET /me — tenant resolution from JWT
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyTenant_ReturnsCallersTenant()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.GetAsync("/api/tenants/me");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<TenantBody>();
        body!.Id.Should().Be(t.TenantId);
        body.Code.Should().Be(t.TenantCode);
        body.LogoUrl.Should().BeNull("freshly-seeded tenant has no logo uploaded yet");
    }

    [Fact]
    public async Task GetMyTenantSettings_ReturnsDefaults_ForFreshTenant()
    {
        // Tenant.Create() auto-seeds CreateDefault settings (7/3 days, orange/red).
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.GetAsync("/api/tenants/me/settings");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<SettingsBody>();
        body!.DefaultWarningDays.Should().Be(7);
        body.DefaultCriticalDays.Should().Be(3);
    }

    [Fact]
    public async Task UpdateMyTenantSettings_AsAdmin_Succeeds_AndPersists()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var update = await client.PutAsJsonAsync("/api/tenants/me/settings", new
        {
            DefaultWarningDays = 14,
            DefaultCriticalDays = 5,
            WarningColor = "#FFCC00",
            CriticalColor = "#CC0000",
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await client.GetAsync("/api/tenants/me/settings");
        var body = await get.Content.ReadFromJsonAsync<SettingsBody>();
        body!.DefaultWarningDays.Should().Be(14);
        body.DefaultCriticalDays.Should().Be(5);
    }

    [Fact]
    public async Task UpdateMyTenantSettings_AsCoordinator_Returns403()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Coordinator);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PutAsJsonAsync("/api/tenants/me/settings", new
        {
            DefaultWarningDays = 14,
            DefaultCriticalDays = 5,
            WarningColor = "#FFCC00",
            CriticalColor = "#CC0000",
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Logo upload — happy path + LogoUrl propagation
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadMyLogo_AsAdmin_PersistsLogoUrl_OnTenant()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync("/api/tenants/me/logo", BuildLogoForm(PngBytes, "logo.png", "image/png"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<TenantBody>();
        body!.LogoUrl.Should().NotBeNullOrEmpty();
        body.LogoUrl.Should().StartWith("tenant-logos/");
        body.LogoUrl.Should().EndWith(".png");
    }

    [Fact]
    public async Task GetMyLogo_AfterUpload_StreamsTheFile()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        var upload = await client.PostAsync("/api/tenants/me/logo", BuildLogoForm(PngBytes, "logo.png", "image/png"));
        upload.EnsureSuccessStatusCode();

        var fetch = await client.GetAsync("/api/tenants/me/logo");
        fetch.StatusCode.Should().Be(HttpStatusCode.OK);
        fetch.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        var bytes = await fetch.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().Be(PngBytes.Length);
    }

    [Fact]
    public async Task GetMyLogo_WhenNoneUploaded_Returns404()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.GetAsync("/api/tenants/me/logo");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteMyLogo_RemovesLogoUrl_AndFile()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);
        await client.PostAsync("/api/tenants/me/logo", BuildLogoForm(PngBytes, "logo.png", "image/png"));

        var del = await client.DeleteAsync("/api/tenants/me/logo");
        del.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await del.Content.ReadFromJsonAsync<TenantBody>();
        body!.LogoUrl.Should().BeNull();

        var fetch = await client.GetAsync("/api/tenants/me/logo");
        fetch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Logo upload — validation surfaces
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadMyLogo_WithBadContentType_Returns400()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        // A renamed script: filename says .png but the MIME is JavaScript.
        // Our content-type check catches it before storage.
        var resp = await client.PostAsync("/api/tenants/me/logo",
            BuildLogoForm(System.Text.Encoding.UTF8.GetBytes("alert(1)"), "logo.png", "application/javascript"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("LOGO_BAD_CONTENT_TYPE");
    }

    [Fact]
    public async Task UploadMyLogo_WithDisallowedExtension_Returns400()
    {
        // Forged content-type passes but a .gif extension is outside our
        // whitelist (png/jpg/jpeg/svg). The second-layer extension check
        // catches this case.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync("/api/tenants/me/logo",
            BuildLogoForm(PngBytes, "logo.gif", "image/png"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var err = await resp.Content.ReadFromJsonAsync<ErrorBody>();
        err!.Error.Code.Should().Be("LOGO_BAD_EXTENSION");
    }

    [Fact]
    public async Task UploadMyLogo_AsCoordinator_Returns403()
    {
        // Coordinator / Manager / Magacioner don't run the company's brand —
        // only Admin / SuperAdmin can change the logo.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Coordinator);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        var resp = await client.PostAsync("/api/tenants/me/logo", BuildLogoForm(PngBytes, "logo.png", "image/png"));
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ──────────────────────────────────────────────────────────────────────
    // /me endpoints — cross-tenant isolation
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadMyLogo_InTenantA_DoesNotLeakInto_TenantB()
    {
        var a = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);
        var b = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Admin);

        var clientA = await TestDataSeeder.AuthenticatedClientAsync(Factory, a);
        await clientA.PostAsync("/api/tenants/me/logo", BuildLogoForm(PngBytes, "logo.png", "image/png"));

        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, b);
        var bGet = await clientB.GetAsync("/api/tenants/me/logo");
        bGet.StatusCode.Should().Be(HttpStatusCode.NotFound, "Tenant B never uploaded a logo, so /me/logo must not leak A's file");

        var bTenant = await clientB.GetFromJsonAsync<TenantBody>("/api/tenants/me");
        bTenant!.LogoUrl.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static MultipartFormDataContent BuildLogoForm(byte[] bytes, string filename, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", filename);
        return form;
    }

    private sealed record TenantBody(Guid Id, string Name, string Code, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, string? LogoUrl);
    private sealed record SettingsBody(Guid Id, Guid TenantId, int DefaultWarningDays, int DefaultCriticalDays, string WarningColor, string CriticalColor);
    private sealed record ErrorBody(ErrorPayload Error);
    private sealed record ErrorPayload(string Code, string Message);
}
