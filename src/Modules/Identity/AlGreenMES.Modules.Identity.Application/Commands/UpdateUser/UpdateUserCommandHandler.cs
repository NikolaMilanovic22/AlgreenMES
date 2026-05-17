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
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public UpdateUserCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IIdentityUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdWithProcessesAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("User", request.Id);

        var oldRole = user.Role;
        var isRoleChange = request.Role != oldRole;
        var isCallerSuperAdmin = _currentUser.IsInRole("SuperAdmin");

        // Sprint 3.0 F-7 — only SuperAdmin can change ANY user's role. Tenant
        // Admins can still edit name/email/active/etc., but the role field is
        // locked to SuperAdmin mutations. Subsumes the older SuperAdmin grant
        // guard but the explicit check below stays as defense-in-depth.
        if (isRoleChange && !isCallerSuperAdmin)
            throw new ForbiddenException("FORBIDDEN_ROLE_CHANGE", "Only SuperAdmin can change a user's role.");

        // Belt-and-suspenders: block escalation to / demotion of SuperAdmin
        // by non-SuperAdmin (already covered above but kept explicit in case
        // F-7 is ever relaxed).
        if ((request.Role == UserRole.SuperAdmin || user.Role == UserRole.SuperAdmin) && !isCallerSuperAdmin)
            throw new ForbiddenException("FORBIDDEN_ROLE_ASSIGNMENT", "Only SuperAdmin can grant or revoke the SuperAdmin role.");

        // Sprint 3.0 F-1 — refuse to demote the last active Admin in a tenant.
        // Tenant lockout is the exact scenario that bit easy-mes (see
        // audit/01_forensics.md). The SuperAdmin platform role is not counted
        // per tenant, so it doesn't help here — Admin is the tenant-level
        // governance role.
        if (oldRole == UserRole.Admin && request.Role != UserRole.Admin)
        {
            var remainingAdmins = await _userRepository.CountActiveByRoleAsync(user.TenantId, UserRole.Admin, cancellationToken);
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

        // Sprint 3.0 F-3 — revoke outstanding refresh tokens when role changes
        // so the affected user can't keep an old-role session alive via refresh.
        // The currently-issued access JWT remains valid until expiry (60 min);
        // tightening further would need a per-user security_stamp claim.
        if (isRoleChange)
            await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return user.Adapt<UserDto>();
    }
}
