using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.UpdateUser;

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand, UserDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserRoleChangeLogRepository _roleChangeLogRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public UpdateUserCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUserRoleChangeLogRepository roleChangeLogRepository,
        IIdentityUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _roleChangeLogRepository = roleChangeLogRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        // Unfiltered lookup so the handler can locate tenantless SuperAdmins
        // and apply the peer-SA guard below. The tenant boundary for non-SA
        // targets is enforced explicitly a few lines down.
        var user = await _userRepository.GetByIdWithProcessesIgnoreFiltersAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("User", request.Id);

        var oldRole = user.Role;
        var isRoleChange = request.Role != oldRole;
        var isCallerSuperAdmin = _currentUser.IsInRole("SuperAdmin");
        var callerUserId = _currentUser.GetCurrentUserId();

        // Cross-tenant boundary. A non-SA caller may only operate on users in
        // their own tenant. Return 404 (not 403) for misses so we don't leak
        // the existence of users in other tenants — same semantic the EF
        // query filter used to produce before SAs went tenantless. SA
        // targets are exempt (they have null TenantId); the peer-SA guard
        // below returns 403 for them instead.
        if (!isCallerSuperAdmin && user.Role != UserRole.SuperAdmin && user.TenantId != _currentUser.GetCurrentTenantId())
            throw new NotFoundException("User", request.Id);

        // Peer SuperAdmin protection (Milos 15.06.2026). No SuperAdmin can
        // edit another SuperAdmin's record — only their own. This shuts down
        // "Bojan as SA deactivates Milos" without trusting any per-form FE
        // disable. Pairs with the same guard in Delete + ResetPassword.
        if (user.Role == UserRole.SuperAdmin && user.Id != callerUserId)
            throw new ForbiddenException("FORBIDDEN_PEER_SUPERADMIN", "A SuperAdmin cannot modify another SuperAdmin's account.");

        // Sprint 3.0 F-7 — only SuperAdmin can change ANY user's role. Tenant
        // Admins can still edit name/email/active/etc., but the role field is
        // locked to SuperAdmin mutations. Subsumes the older SuperAdmin grant
        // guard but the explicit check below stays as defense-in-depth.
        if (isRoleChange && !isCallerSuperAdmin)
            throw new ForbiddenException("FORBIDDEN_ROLE_CHANGE", "Only SuperAdmin can change a user's role.");

        // SuperAdmin is platform-level and may only be granted/revoked
        // directly in the database (Milos 12.06.2026 — "that option nobody
        // can grant, it is granted only directly in DB"). Block any role
        // CHANGE that crosses the SuperAdmin boundary on either side, even
        // when the caller is a SuperAdmin — otherwise a compromised
        // SuperAdmin session could quietly demote or promote others.
        // Name/email/active updates on a SuperAdmin user are still allowed
        // (oldRole == newRole == SuperAdmin → not a role change).
        if (isRoleChange && (request.Role == UserRole.SuperAdmin || oldRole == UserRole.SuperAdmin))
            throw new ForbiddenException("FORBIDDEN_ROLE_ASSIGNMENT", "The SuperAdmin role can only be granted or revoked directly in the database.");

        // Sprint 3.0 F-1 — refuse to demote the last active Admin in a tenant.
        // Tenant lockout is the exact scenario that bit easy-mes (see
        // audit/01_forensics.md). The SuperAdmin platform role is not counted
        // per tenant, so it doesn't help here — Admin is the tenant-level
        // governance role.
        if (oldRole == UserRole.Admin && request.Role != UserRole.Admin)
        {
            var remainingAdmins = await _userRepository.CountActiveByRoleAsync(user.TenantIdRequired, UserRole.Admin, cancellationToken);
            // The target is included in remainingAdmins if currently active.
            // After demotion the count drops by 1 — block if that would hit 0.
            var effectiveRemaining = user.IsActive ? remainingAdmins - 1 : remainingAdmins;
            if (effectiveRemaining <= 0)
                throw new DomainException("LAST_ADMIN_REMOVAL", "Cannot remove the last active Admin from the tenant.");
        }

        user.Update(request.FirstName, request.LastName, request.Role, request.IsActive, request.CanIncludeWithdrawnInAnalysis);

        if (request.Role == UserRole.Department && request.ProcessIds != null)
            user.AssignProcesses(request.TenantId, request.ProcessIds);
        else if (request.Role != UserRole.Department)
            user.AssignProcesses(request.TenantId, []);

        // Multi-role assignment (Saša 08.06.2026). Null = leave existing
        // additional roles alone; non-null = replace them with the given
        // list. Same caller-authorisation gate as primary role.
        if (request.AdditionalRoles != null)
        {
            if (!isCallerSuperAdmin)
                throw new ForbiddenException("FORBIDDEN_ROLE_CHANGE", "Only SuperAdmin can change a user's additional roles.");
            user.AssignAdditionalRoles(request.TenantId, request.AdditionalRoles);
        }

        // Sprint 3.0 F-3 — revoke outstanding refresh tokens when role changes
        // so the affected user can't keep an old-role session alive via refresh.
        if (isRoleChange || request.AdditionalRoles != null)
            await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, cancellationToken);

        // F-9 — primary role transition history. Captures the (old, new,
        // who, when) tuple so an investigator can reconstruct the role
        // trajectory of a user. AdditionalRoles changes intentionally not
        // logged here — the F-9 spec is keyed on the single primary role,
        // and a separate audit table for the multi-role set is out of scope.
        if (isRoleChange)
        {
            var changedByUserId = _currentUser.GetCurrentUserId();
            var logEntry = UserRoleChangeLog.Create(
                user.TenantIdRequired, user.Id, oldRole, request.Role, changedByUserId, DateTime.UtcNow);
            await _roleChangeLogRepository.AddAsync(logEntry, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return user.Adapt<UserDto>();
    }
}
