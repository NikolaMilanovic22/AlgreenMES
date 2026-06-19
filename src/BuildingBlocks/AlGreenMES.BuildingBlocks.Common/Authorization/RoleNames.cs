namespace AlGreenMES.BuildingBlocks.Common.Authorization;

/// <summary>
/// Canonical role name strings. Use these constants instead of inline
/// magic strings in <c>[Authorize(Roles = ...)]</c> attributes and
/// <c>IsInRole(...)</c> checks. A typo in the string form silently
/// flips authorization the wrong way — the check never fires and the
/// caller is treated as if they didn't have the role. Match the values
/// to the <c>UserRole</c> enum names exactly.
/// </summary>
public static class RoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Coordinator = "Coordinator";
    public const string SalesManager = "SalesManager";
    public const string Department = "Department";
}
