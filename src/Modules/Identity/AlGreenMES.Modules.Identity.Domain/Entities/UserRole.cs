namespace AlGreenMES.Modules.Identity.Domain.Entities;

public enum UserRole
{
    Admin,
    Manager,
    Coordinator,
    SalesManager,
    Department,
    SuperAdmin,
    // Magacioner — warehouse worker. Saša 08.06.2026: per-user role,
    // assignable IN ADDITION to other roles (a Coordinator can also be
    // Magacioner). See UserRoleAssignment for the multi-role mechanism.
    Magacioner
}
