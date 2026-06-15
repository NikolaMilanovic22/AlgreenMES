using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlGreenMES.Modules.Tenancy.Domain.Entities;

public class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>
    /// Relative path to the tenant's uploaded brand logo (e.g.
    /// "tenant-logos/{tenantId}.png"). Resolved to a full URL by the
    /// API endpoint that streams the file. Null when the tenant hasn't
    /// uploaded a logo yet; FE falls back to the MPMS mark.
    /// </summary>
    public string? LogoUrl { get; private set; }

    public TenantSettings? Settings { get; private set; }

    private Tenant()
    {
    }

    public static Tenant Create(string name, string code)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("TENANT_NAME_REQUIRED", "Tenant name is required.");

        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("TENANT_CODE_REQUIRED", "Tenant code is required.");

        if (code.Length > 50)
            throw new DomainException("TENANT_CODE_TOO_LONG", "Tenant code must be 50 characters or less.");

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        tenant.Settings = TenantSettings.CreateDefault(tenant.Id);

        return tenant;
    }

    public void Update(string name, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("TENANT_NAME_REQUIRED", "Tenant name is required.");

        Name = name.Trim();
        IsActive = isActive;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetLogoUrl(string? logoUrl)
    {
        LogoUrl = logoUrl;
        UpdatedAt = DateTime.UtcNow;
    }
}
