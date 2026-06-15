using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Orders.Infrastructure.Services;

/// <summary>
/// Reads Identity.UserProcesses (the worker's qualified process catalog IDs)
/// directly via the IdentityDbContext — same cross-module pattern used by
/// ReportingQueryService for Shifts. Bypasses the OrdersDbContext tenant
/// filter by going through the Identity context's own filter, which the
/// synthetic HttpContext established by AutoLogoutBackgroundService satisfies.
/// </summary>
public class UserProcessLookup : IUserProcessLookup
{
    private readonly IdentityDbContext _identityDb;

    public UserProcessLookup(IdentityDbContext identityDb)
    {
        _identityDb = identityDb;
    }

    public async Task<IReadOnlyList<Guid>> GetUserProcessIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters in case this is called from a BG-service-style
        // path where the tenant filter would otherwise blank the result;
        // user_id is unique so the filter wouldn't add safety.
        return await _identityDb.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .SelectMany(u => u.UserProcesses.Select(up => up.ProcessId))
            .ToListAsync(cancellationToken);
    }
}
