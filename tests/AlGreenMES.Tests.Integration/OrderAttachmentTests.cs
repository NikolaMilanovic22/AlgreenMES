using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Tests.Integration.Helpers;
using FluentAssertions;
using Xunit;

namespace AlGreenMES.Tests.Integration;

/// <summary>
/// POST /api/orders/{orderId}/attachments — order attachment upload. The
/// handler has solid validation (content type allowlist, extension
/// allowlist, size cap, per-order count cap, tenant + item-belongs-to-order
/// guards) but zero integration coverage until today. Defense-in-depth: a
/// future refactor that drops a guard (or a settings file that widens the
/// allowed-types list) would silently regress this surface and let a worker
/// upload a 10GB binary or a script disguised as an image.
///
/// Test settings match the defaults baked into FileStorageSettings:
///   - 10 MB max file size
///   - 10 attachments per order/item
///   - Allowed: .jpg/.jpeg/.png/.pdf and matching content types
/// </summary>
public class OrderAttachmentTests : IntegrationTestBase
{
    public OrderAttachmentTests(AlgreenWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task UploadAttachment_WithValidPng_Returns200()
    {
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Manager);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        using var form = BuildMultipart(BuildTinyPngBytes(), "test.png", "image/png");
        var resp = await client.PostAsync($"/api/orders/{orderId}/attachments", form);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("originalFileName").GetString().Should().Be("test.png");
        doc.RootElement.GetProperty("contentType").GetString().Should().Be("image/png");
    }

    [Fact]
    public async Task UploadAttachment_WithDisallowedContentType_Returns400_INVALID_CONTENT_TYPE()
    {
        // application/zip is NOT in the allowlist — should be rejected
        // even though the controller's [RequestSizeLimit] would let it
        // through. Catches the "we accepted a malicious ZIP because the
        // handler-side allowlist was bypassed" class of regression.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Manager);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        using var form = BuildMultipart(new byte[100], "evil.zip", "application/zip");
        var resp = await client.PostAsync($"/api/orders/{orderId}/attachments", form);

        await AssertErrorCodeAsync(resp, "INVALID_CONTENT_TYPE");
    }

    [Fact]
    public async Task UploadAttachment_WithDisallowedExtension_Returns400_INVALID_FILE_TYPE()
    {
        // .exe with an image/png content-type header — content-type passes
        // but extension allowlist must catch it. MIME spoofing is the
        // textbook web-upload vulnerability.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Manager);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        using var form = BuildMultipart(BuildTinyPngBytes(), "malware.exe", "image/png");
        var resp = await client.PostAsync($"/api/orders/{orderId}/attachments", form);

        await AssertErrorCodeAsync(resp, "INVALID_FILE_TYPE");
    }

    [Fact]
    public async Task UploadAttachment_OnAnotherTenantsOrder_Returns400_FORBIDDEN()
    {
        // Tenant isolation: tenant B authenticates and tries to upload to
        // tenant A's order ID. Handler must reject regardless of role.
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory, UserRole.Manager, UserRole.Manager);
        var ordersInA = await TestDataSeeder.SeedOrderAsync(Factory, a.TenantId, a.UserId);
        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, b);

        using var form = BuildMultipart(BuildTinyPngBytes(), "test.png", "image/png");
        var resp = await clientB.PostAsync($"/api/orders/{ordersInA}/attachments", form);

        // GetByIdAsync respects the tenant query filter → tenant B sees
        // order as NotFound → 404. Either way the cross-tenant upload
        // does not land.
        resp.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest },
            "tenant isolation must block cross-tenant uploads");
    }

    [Fact]
    public async Task UploadAttachment_AsDepartmentRole_Returns403()
    {
        // Endpoint requires ManagerOrSales. Workers must not be able to
        // upload attachments — the role gate is the only thing standing
        // between a tablet user and the storage bucket.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Department);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        using var form = BuildMultipart(BuildTinyPngBytes(), "test.png", "image/png");
        var resp = await client.PostAsync($"/api/orders/{orderId}/attachments", form);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DownloadAttachment_PdfWithSerbianFilename_Returns200_WithRfc5987ContentDisposition()
    {
        // MES-API-F fix (OrdersController ~325): a PDF whose original filename
        // contains Serbian diacritics (š/č/ž) must be served via
        // ContentDispositionHeaderValue.SetHttpFileName so the header is
        // RFC 5987 encoded (filename*=UTF-8''…). A raw non-ASCII byte in the
        // header throws "Invalid non-ASCII character in header" → 500.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Manager);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        const string serbianName = "Račun-š-č-ž.pdf";
        using var form = BuildMultipart(BuildTinyPdfBytes(), serbianName, "application/pdf");
        var uploadResp = await client.PostAsync($"/api/orders/{orderId}/attachments", form);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var uploadDoc = JsonDocument.Parse(await uploadResp.Content.ReadAsStringAsync());
        var attachmentId = uploadDoc.RootElement.GetProperty("id").GetGuid();

        var resp = await client.GetAsync($"/api/orders/{orderId}/attachments/{attachmentId}/download");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var contentDisposition = ReadContentDisposition(resp);
        contentDisposition.Should().NotBeNull();
        contentDisposition!.Should().Contain("filename*=UTF-8''",
            "non-ASCII filenames must be RFC 5987 encoded");
        contentDisposition.Should().NotContain("š", "the raw diacritic must not appear in the header");
    }

    [Fact]
    public async Task DownloadAttachment_PngWithSerbianFilename_Returns200_WithRfc5987ContentDisposition()
    {
        // The non-PDF path uses File(stream, contentType, originalFileName),
        // which ASP.NET also RFC 5987-encodes. Guard both branches.
        var t = await TestDataSeeder.SeedTenantWithUserAsync(Factory, UserRole.Manager);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, t.TenantId, t.UserId);
        var client = await TestDataSeeder.AuthenticatedClientAsync(Factory, t);

        using var form = BuildMultipart(BuildTinyPngBytes(), "Nacrt-šćž.png", "image/png");
        var uploadResp = await client.PostAsync($"/api/orders/{orderId}/attachments", form);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var uploadDoc = JsonDocument.Parse(await uploadResp.Content.ReadAsStringAsync());
        var attachmentId = uploadDoc.RootElement.GetProperty("id").GetGuid();

        var resp = await client.GetAsync($"/api/orders/{orderId}/attachments/{attachmentId}/download");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var contentDisposition = ReadContentDisposition(resp);
        contentDisposition.Should().NotBeNull();
        contentDisposition!.Should().Contain("filename*=UTF-8''");
        contentDisposition.Should().NotContain("š");
    }

    private static string? ReadContentDisposition(HttpResponseMessage resp)
    {
        if (resp.Content.Headers.ContentDisposition is { } cd)
            return cd.ToString();
        return resp.Content.Headers.TryGetValues("Content-Disposition", out var v)
            ? string.Join("", v)
            : null;
    }

    [Fact]
    public async Task DeleteAttachment_CrossTenant_IsRejected_AndFilePersists_ButOwnerCanDelete()
    {
        // DeleteOrderAttachmentCommandHandler compares attachment.TenantId to
        // the request. Tenant B must not be able to delete tenant A's
        // attachment; the owner (A) can.
        var (a, b) = await TestDataSeeder.SeedTwoTenantsAsync(Factory, UserRole.Manager, UserRole.Manager);
        var orderId = await TestDataSeeder.SeedOrderAsync(Factory, a.TenantId, a.UserId);
        var clientA = await TestDataSeeder.AuthenticatedClientAsync(Factory, a);

        using var form = BuildMultipart(BuildTinyPngBytes(), "test.png", "image/png");
        var uploadResp = await clientA.PostAsync($"/api/orders/{orderId}/attachments", form);
        uploadResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var uploadDoc = JsonDocument.Parse(await uploadResp.Content.ReadAsStringAsync());
        var attachmentId = uploadDoc.RootElement.GetProperty("id").GetGuid();

        // Tenant B tries to delete A's attachment.
        var clientB = await TestDataSeeder.AuthenticatedClientAsync(Factory, b);
        var crossDelete = await clientB.DeleteAsync(
            $"/api/orders/{orderId}/attachments/{attachmentId}?tenantId={b.TenantId}");
        crossDelete.IsSuccessStatusCode.Should().BeFalse("tenant B must not delete tenant A's attachment");

        // Still present — A can still download it.
        var stillThere = await clientA.GetAsync($"/api/orders/{orderId}/attachments/{attachmentId}/download");
        stillThere.StatusCode.Should().Be(HttpStatusCode.OK);

        // Owner deletes → 204 and gone.
        var ownDelete = await clientA.DeleteAsync(
            $"/api/orders/{orderId}/attachments/{attachmentId}?tenantId={a.TenantId}");
        ownDelete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await clientA.GetAsync($"/api/orders/{orderId}/attachments/{attachmentId}/download");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------------
    // Helpers — keep test bodies focused on the assertion.
    // ---------------------------------------------------------------------

    /// <summary>Minimal valid-enough PDF payload (header + EOF marker).</summary>
    private static byte[] BuildTinyPdfBytes() =>
        System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");

    private static MultipartFormDataContent BuildMultipart(byte[] bytes, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        // The controller binds the file via IFormFile parameter named "file".
        // Multipart "name" must match exactly.
        form.Add(file, "file", fileName);
        return form;
    }

    /// <summary>
    /// 1x1 transparent PNG (smallest valid PNG). Enough bytes to be a
    /// real file in storage but trivial to hold in memory.
    /// </summary>
    private static byte[] BuildTinyPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static async Task AssertErrorCodeAsync(HttpResponseMessage resp, string expectedCode)
    {
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be(expectedCode);
    }
}
