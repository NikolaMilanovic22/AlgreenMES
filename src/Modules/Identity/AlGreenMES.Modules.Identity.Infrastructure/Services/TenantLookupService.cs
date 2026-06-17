using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Identity.Infrastructure.Services;

public class TenantLookupService : ITenantLookupService
{
    private readonly TenancyDbContext _tenancyDbContext;

    public TenantLookupService(TenancyDbContext tenancyDbContext)
    {
        _tenancyDbContext = tenancyDbContext;
    }

    public async Task<TenantLookupResult?> GetTenantByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        var tenant = await _tenancyDbContext.Tenants
            .FirstOrDefaultAsync(t => t.Code == normalizedCode, cancellationToken);

        if (tenant is null)
            return null;

        return new TenantLookupResult(tenant.Id, tenant.Code, tenant.IsActive, tenant.BlockedAt.HasValue);
    }
}
