using AlGreenMES.BuildingBlocks.Common.Interfaces;

namespace AlgreenMES.API.Services;

public class TenantService : ITenantService
{
    private readonly ICurrentUserService _currentUser;

    public TenantService(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    public Guid GetCurrentTenantId() => _currentUser.GetCurrentTenantId();
}
