namespace AlGreenMES.Modules.Tenancy.Application.Services;

public interface ITenantLogoStorage
{
    /// <summary>
    /// Persist the uploaded logo for the given tenant. Returns the relative
    /// path stored on Tenant.LogoUrl (e.g. "tenant-logos/{tenantId}.png").
    /// </summary>
    Task<string> SaveAsync(Guid tenantId, Stream fileStream, string extension, CancellationToken cancellationToken = default);

    /// <summary>Open the stored logo for streaming. Returns null when missing.</summary>
    Task<(Stream Stream, string ContentType)?> GetAsync(string relativePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Allowed extensions (lowercase, leading dot) — used by the controller to 400 early.</summary>
    IReadOnlySet<string> AllowedExtensions { get; }

    /// <summary>
    /// Allowed MIME types (lowercase). Checked alongside the extension so an
    /// attacker can't rename a JS file to .png and bypass the whitelist.
    /// </summary>
    IReadOnlySet<string> AllowedContentTypes { get; }

    /// <summary>Max file size in bytes.</summary>
    long MaxFileSizeBytes { get; }
}
