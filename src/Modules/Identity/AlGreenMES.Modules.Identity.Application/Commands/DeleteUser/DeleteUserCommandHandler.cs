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
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        var callerUserId = _currentUser.GetCurrentUserId();
        var isCallerSuperAdmin = _currentUser.IsInRole("SuperAdmin");

        // Sprint 3.0 F-2a — cannot delete yourself. Even a SuperAdmin should
        // not delete their own row in a single click; demote first or have
        // another SuperAdmin do it.
        if (user.Id == callerUserId)
            throw new ForbiddenException("SELF_DELETE_FORBIDDEN", "You cannot delete your own user.");

        // Sprint 3.0 F-2b — only SuperAdmin can delete a SuperAdmin user.
        if (user.Role == UserRole.SuperAdmin && !isCallerSuperAdmin)
            throw new ForbiddenException("FORBIDDEN_SUPERADMIN_DELETE", "Only SuperAdmin can delete a SuperAdmin user.");

        // Sprint 3.0 F-2c — refuse to delete the last active Admin in a tenant
        // (tenant lockout, same scenario as F-1).
        if (user.Role == UserRole.Admin)
        {
            var remainingAdmins = await _userRepository.CountActiveByRoleAsync(user.TenantId, UserRole.Admin, cancellationToken);
            var effectiveRemaining = user.IsActive ? remainingAdmins - 1 : remainingAdmins;
            if (effectiveRemaining <= 0)
                throw new DomainException("LAST_ADMIN_REMOVAL", "Cannot remove the last active Admin from the tenant.");
        }

        _userRepository.Delete(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
