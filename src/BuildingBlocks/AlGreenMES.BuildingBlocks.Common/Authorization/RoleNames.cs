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
    public const string Magacioner = "Magacioner";
}

/// <summary>
/// Pre-baked role combinations for <c>[Authorize(Roles = ...)]</c>. C# attributes
/// require a <c>const string</c>, so we can't compose these at runtime — but we
/// CAN reference a single named constant per group, which (a) makes intent
/// readable at the controller and (b) catches drift like the existing
/// <c>"Admin,SuperAdmin"</c> mis-ordering vs <c>"SuperAdmin,Admin"</c>
/// elsewhere (functionally the same; visually inconsistent).
///
/// Group naming follows seniority: "AdminUp" = SuperAdmin + Admin,
/// "ManagerUp" = + Manager, etc.
/// </summary>
public static class RoleGroups
{
    public const string SuperAdminOnly = RoleNames.SuperAdmin;
    public const string AdminUp = RoleNames.SuperAdmin + "," + RoleNames.Admin;
    public const string ManagerUp = RoleNames.SuperAdmin + "," + RoleNames.Admin + "," + RoleNames.Manager;
    public const string CoordinatorUp = RoleNames.SuperAdmin + "," + RoleNames.Admin + "," + RoleNames.Manager + "," + RoleNames.Coordinator;
    public const string ManagerOrSales = RoleNames.SuperAdmin + "," + RoleNames.Admin + "," + RoleNames.Manager + "," + RoleNames.SalesManager;
    public const string ManagerOrWarehouse = RoleNames.SuperAdmin + "," + RoleNames.Admin + "," + RoleNames.Manager + "," + RoleNames.Magacioner;
    public const string ProductionFloor = RoleNames.SuperAdmin + "," + RoleNames.Admin + "," + RoleNames.Manager + "," + RoleNames.Coordinator + "," + RoleNames.Magacioner;
    /// <summary>Every role in the system. Use for /me-style endpoints anyone can hit.</summary>
    public const string AnyAuthenticated = RoleNames.SuperAdmin + "," + RoleNames.Admin + "," + RoleNames.Manager + "," + RoleNames.Coordinator + "," + RoleNames.SalesManager + "," + RoleNames.Department + "," + RoleNames.Magacioner;
}
