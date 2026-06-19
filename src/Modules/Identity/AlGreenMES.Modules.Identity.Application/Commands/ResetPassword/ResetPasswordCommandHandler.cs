using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.ResetPassword;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Unit>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUser;

    public ResetPasswordCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IIdentityUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUser)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        // Unfiltered lookup so the peer-SA guard below can fire for tenantless
        // SuperAdmin targets. The cross-tenant boundary for non-SA targets is
        // enforced explicitly right after.
        var user = await _userRepository.GetByIdWithProcessesIgnoreFiltersAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        // Cross-tenant boundary. SA targets are exempt — they have null
        // TenantId; the peer-SA guard below returns 403 for them instead.
        var isCallerSuperAdmin = _currentUser.IsInRole("SuperAdmin");
        if (!isCallerSuperAdmin && user.Role != UserRole.SuperAdmin && user.TenantId != _currentUser.GetCurrentTenantId())
            throw new NotFoundException("User", request.UserId);

        // Peer SuperAdmin protection (Milos 15.06.2026). SuperAdmin passwords
        // can only be changed by the owner through ChangePassword (which
        // verifies the current password) — never by another admin via this
        // reset path. Prevents "Bojan resets Milos's password and locks him
        // out". Owner changes their password via /me/change-password.
        if (user.Role == UserRole.SuperAdmin)
            throw new ForbiddenException("FORBIDDEN_PEER_SUPERADMIN", "A SuperAdmin password cannot be reset by another admin.");

        var newPasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.ChangePassword(newPasswordHash);

        // F-12 — admin-initiated password reset must also drop any refresh
        // tokens for the target user. The usual scenario is an admin
        // resetting a compromised user's password — without this the
        // attacker's session would survive for the 7-day refresh TTL.
        await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
