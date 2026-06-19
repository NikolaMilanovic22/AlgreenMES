namespace AlGreenMES.BuildingBlocks.Common.Interfaces;

public interface ICurrentUserService
{
    Guid GetCurrentTenantId();
    Guid GetCurrentUserId();
    bool IsInRole(string role);
    /// <summary>
    /// Typed shortcut for <c>IsInRole(RoleNames.SuperAdmin)</c>. Use this
    /// instead of the magic-string form anywhere in module handlers — a typo
    /// in the string version silently flips authorization to false (the SA
    /// case never fires and the handler treats them as a regular user).
    /// </summary>
    bool IsSuperAdmin { get; }
    bool IsAuthenticated { get; }
}
