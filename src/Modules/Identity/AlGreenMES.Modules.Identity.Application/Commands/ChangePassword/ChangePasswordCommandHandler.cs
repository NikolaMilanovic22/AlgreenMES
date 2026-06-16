using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.ChangePassword;

public class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Unit>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUser;

    public ChangePasswordCommandHandler(
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

    public async Task<Unit> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        // Change-password is the self-service flow — strictly self-only for
        // EVERY role, including SuperAdmin (Milos 16.06.2026 tightening).
        // SuperAdmins previously got a back door here that let them change
        // any user's password; that conflicted with peer-SA protection and
        // gave them an avenue to mutate tenant credentials. The admin
        // equivalent for non-SA users is /reset-password (handler rejects
        // the SA target separately via FORBIDDEN_PEER_SUPERADMIN).
        var callerUserId = _currentUser.GetCurrentUserId();
        if (request.UserId != callerUserId)
            throw new ForbiddenException("CHANGE_PASSWORD_NOT_SELF", "You can only change your own password.");

        // IgnoreFilters because SuperAdmin users have a null TenantId and
        // wouldn't match the tenant query filter scoped to whichever
        // tenant they're currently browsing — but we've already verified
        // request.UserId == callerUserId (the JWT subject), so the lookup
        // is safe to widen.
        var user = await _userRepository.GetByIdIgnoreFiltersAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            throw new DomainException("INVALID_CURRENT_PASSWORD", "The current password is incorrect.");

        var newPasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.ChangePassword(newPasswordHash);

        // F-12 — match the role-change flow (F-3): a password change must
        // invalidate any refresh token issued under the old credentials. JWT
        // access tokens still work until their 60-min TTL, but the user (or
        // attacker holding a stolen session) can't roll over into a fresh
        // token once the old refresh is revoked.
        await _refreshTokenRepository.RevokeAllForUserAsync(user.Id, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
