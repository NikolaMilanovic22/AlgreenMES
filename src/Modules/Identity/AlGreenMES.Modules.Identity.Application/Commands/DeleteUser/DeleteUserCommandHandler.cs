using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.DeleteUser;

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand, Unit>
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public DeleteUserCommandHandler(
        IUserRepository userRepository,
        IIdentityUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        // Unfiltered lookup so the handler can locate tenantless SuperAdmins
        // and apply the peer-SA guard below. The tenant boundary for non-SA
        // targets is enforced explicitly a few lines down.
        var user = await _userRepository.GetByIdWithProcessesIgnoreFiltersAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        var callerUserId = _currentUser.GetCurrentUserId();
        var isCallerSuperAdmin = _currentUser.IsInRole("SuperAdmin");

        // Cross-tenant boundary. A non-SA caller may only delete users in
        // their own tenant. Return 404 for misses so we don't leak the
        // existence of users in other tenants — same semantic the EF query
        // filter used to produce before SAs went tenantless. SA targets are
        // intentionally exempt from this check (they have null TenantId);
        // the peer-SA guard below returns 403 for them instead.
        if (!isCallerSuperAdmin && user.Role != UserRole.SuperAdmin && user.TenantId != _currentUser.GetCurrentTenantId())
            throw new NotFoundException("User", request.UserId);

        // Sprint 3.0 F-2a — cannot delete yourself. Even a SuperAdmin should
        // not delete their own row in a single click; demote first or have
        // another SuperAdmin do it.
        if (user.Id == callerUserId)
            throw new ForbiddenException("SELF_DELETE_FORBIDDEN", "You cannot delete your own user.");

        // Peer SuperAdmin protection (Milos 15.06.2026). Combined with the
        // self-delete block above this means SuperAdmin users are never
        // deletable via the API — reactivation/removal is intentionally
        // a DB-only operation. The older FORBIDDEN_SUPERADMIN_DELETE check
        // is subsumed by this stronger rule.
        if (user.Role == UserRole.SuperAdmin)
            throw new ForbiddenException("FORBIDDEN_PEER_SUPERADMIN", "A SuperAdmin account cannot be deleted via the API.");

        // Sprint 3.0 F-2c — refuse to delete the last active Admin in a tenant
        // (tenant lockout, same scenario as F-1).
        if (user.Role == UserRole.Admin)
        {
            var remainingAdmins = await _userRepository.CountActiveByRoleAsync(user.TenantIdRequired, UserRole.Admin, cancellationToken);
            var effectiveRemaining = user.IsActive ? remainingAdmins - 1 : remainingAdmins;
            if (effectiveRemaining <= 0)
                throw new DomainException("LAST_ADMIN_REMOVAL", "Cannot remove the last active Admin from the tenant.");
        }

        _userRepository.Delete(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
