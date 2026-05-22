namespace AlGreenMES.BuildingBlocks.Common.Interfaces;

public interface ICurrentUserService
{
    Guid GetCurrentTenantId();
    Guid GetCurrentUserId();
    bool IsInRole(string role);
    bool IsAuthenticated { get; }
}
