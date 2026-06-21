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
    /// Set when a SuperAdmin manually blocks the tenant (typically for
    /// unpaid subscription). Block flips IsActive to false; Unblock flips
    /// it back. The reason is shown only to SAs in the Naplata tab.
    /// </summary>
    public DateTime? BlockedAt { get; private set; }
    public string? BlockedReason { get; private set; }

    /// <summary>
    /// Relative path to the tenant's uploaded brand logo (e.g.
    /// "tenant-logos/{tenantId}.png"). Resolved to a full URL by the
    /// API endpoint that streams the file. Null when the tenant hasn't
    /// uploaded a logo yet; FE falls back to the MPMS mark.
    /// </summary>
    public string? LogoUrl { get; private set; }

    /// <summary>
    /// Feature keys the SuperAdmin has disabled for this tenant. Empty
    /// means "everything enabled". New tenants ship on the Basic plan
    /// (see <see cref="BasicPlanDisabledFeatures"/>) — process times and
    /// the magacin module are opt-in. SAs adjust via PUT /tenants/{id}/features.
    /// </summary>
    public List<string> DisabledFeatures { get; private set; } = new();

    public TenantSettings? Settings { get; private set; }

    /// <summary>Features that the Basic subscription tier doesn't include by default (Saša 17.06.2026).</summary>
    public static IReadOnlyList<string> BasicPlanDisabledFeatures { get; } = new[] { "process-times", "magacin" };

    /// <summary>Feature keys the FE understands. Kept in sync with the menu definition in SidebarMenu.tsx.</summary>
    public static IReadOnlyList<string> KnownFeatures { get; } = new[] { "process-times", "magacin" };

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
            CreatedAt = DateTime.UtcNow,
            DisabledFeatures = BasicPlanDisabledFeatures.ToList(),
        };

        tenant.Settings = TenantSettings.CreateDefault(tenant.Id);

        return tenant;
    }

    public void SetDisabledFeatures(IEnumerable<string> disabled)
    {
        // Reject unknown feature keys so a typo in the UI can't silently
        // disable nothing (or enable everything by mistake).
        var normalised = disabled
            .Select(f => (f ?? string.Empty).Trim().ToLowerInvariant())
            .Where(f => f.Length > 0)
            .Distinct()
            .ToList();

        var unknown = normalised.Where(f => !KnownFeatures.Contains(f)).ToList();
        if (unknown.Count > 0)
            throw new DomainException("UNKNOWN_FEATURE", $"Unknown feature key(s): {string.Join(", ", unknown)}");

        DisabledFeatures = normalised;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("TENANT_NAME_REQUIRED", "Tenant name is required.");

        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetLogoUrl(string? logoUrl)
    {
        LogoUrl = logoUrl;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Block(string? reason)
    {
        IsActive = false;
        BlockedAt = DateTime.UtcNow;
        BlockedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unblock()
    {
        IsActive = true;
        BlockedAt = null;
        BlockedReason = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
