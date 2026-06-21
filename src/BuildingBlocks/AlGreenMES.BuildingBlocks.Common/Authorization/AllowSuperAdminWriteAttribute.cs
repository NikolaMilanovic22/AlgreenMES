namespace AlGreenMES.BuildingBlocks.Common.Authorization;

/// <summary>
/// Opt-out marker for <see cref="SuperAdminReadOnlyMiddleware"/>. Apply to
/// controllers or actions that a SuperAdmin must be able to call with a
/// write verb (POST/PUT/DELETE/PATCH) — typically platform-level
/// operations the SuperAdmin has explicit authority over:
/// <list type="bullet">
///   <item>Create / update / deactivate tenants.</item>
///   <item>Create / list other SuperAdmins.</item>
///   <item>Change one's own password.</item>
/// </list>
/// Everything else stays blocked so a stray click in a foreign tenant
/// can't accidentally mutate that tenant's production data.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AllowSuperAdminWriteAttribute : Attribute
{
}
