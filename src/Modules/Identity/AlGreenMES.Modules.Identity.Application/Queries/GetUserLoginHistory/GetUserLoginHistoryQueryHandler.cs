using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Queries.GetUserLoginHistory;

public class GetUserLoginHistoryQueryHandler : IRequestHandler<GetUserLoginHistoryQuery, IReadOnlyList<LoginAttemptDto>>
{
    /// <summary>
    /// LoginAttempt is keyed by email + tenant_id, not user_id (failed
    /// pre-auth attempts on a wrong email can't resolve a user). So we
    /// look up the user by id to get their (tenant, email) pair, then
    /// query attempts for that pair.
    /// </summary>
    private readonly IUserRepository _userRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;

    public GetUserLoginHistoryQueryHandler(
        IUserRepository userRepository,
        ILoginAttemptRepository loginAttemptRepository)
    {
        _userRepository = userRepository;
        _loginAttemptRepository = loginAttemptRepository;
    }

    public async Task<IReadOnlyList<LoginAttemptDto>> Handle(GetUserLoginHistoryQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        // Cap the limit defensively — even with no UI control we don't
        // want a misbehaving client pulling 100K rows in one go.
        var capped = Math.Clamp(request.Limit, 1, 100);

        var rows = await _loginAttemptRepository.GetRecentForEmailAsync(
            user.TenantIdRequired, user.Email, capped, cancellationToken);

        return rows
            .Select(la => new LoginAttemptDto(
                la.Id,
                la.Email,
                la.IpAddress,
                la.UserAgent,
                la.Succeeded,
                la.FailureReason,
                la.AttemptedAt))
            .ToList();
    }
}
