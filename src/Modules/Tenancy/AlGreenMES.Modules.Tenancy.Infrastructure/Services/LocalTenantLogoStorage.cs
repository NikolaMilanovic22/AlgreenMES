using AlGreenMES.Modules.Tenancy.Application.Services;
using Microsoft.Extensions.Configuration;

namespace AlGreenMES.Modules.Tenancy.Infrastructure.Services;

/// <summary>
/// Filesystem-backed implementation of <see cref="ITenantLogoStorage"/>.
/// Stores each tenant's logo at <c>{BasePath}/tenant-logos/{tenantId}{ext}</c>
/// — a single slot per tenant, overwritten on re-upload (no version history).
///
/// Reuses the <c>FileStorage:BasePath</c> config the Orders module already
/// wires up so deploys don't need a second storage location. Size + extension
/// caps are intentionally tighter than order attachments: logos are tiny
/// brand marks rendered in the sidebar, not full documents.
/// </summary>
public sealed class LocalTenantLogoStorage : ITenantLogoStorage
{
    private const string SubDirectory = "tenant-logos";

    private readonly string _basePath;

    public LocalTenantLogoStorage(IConfiguration configuration)
    {
        var configured = configuration["FileStorage:BasePath"] ?? "./uploads";
        _basePath = Path.GetFullPath(configured);
        Directory.CreateDirectory(Path.Combine(_basePath, SubDirectory));
    }

    public IReadOnlySet<string> AllowedExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".svg" };

    public IReadOnlySet<string> AllowedContentTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/svg+xml" };

    public long MaxFileSizeBytes => 2 * 1024 * 1024;

    public async Task<string> SaveAsync(Guid tenantId, Stream fileStream, string extension, CancellationToken cancellationToken = default)
    {
        var ext = extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"Extension '{ext}' is not allowed.");

        // Clean up any stale logo for this tenant under a different extension
        // (e.g. user switches from .png to .jpg) so we don't accumulate
        // orphan files. We could keep them around for rollback, but a logo
        // doesn't need that and the cleanup keeps the directory tidy.
        foreach (var stale in Directory.EnumerateFiles(Path.Combine(_basePath, SubDirectory), $"{tenantId}.*"))
            File.Delete(stale);

        var relative = Path.Combine(SubDirectory, $"{tenantId}{ext}").Replace('\\', '/');
        var fullPath = GetSafePath(relative);

        await using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await fileStream.CopyToAsync(output, cancellationToken);

        return relative;
    }

    public Task<(Stream Stream, string ContentType)?> GetAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafePath(relativePath);
        if (!File.Exists(fullPath))
            return Task.FromResult<(Stream, string)?>(null);

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<(Stream Stream, string ContentType)?>((stream, contentType));
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafePath(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    private string GetSafePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, relativePath));
        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid file path.");
        return fullPath;
    }
}
