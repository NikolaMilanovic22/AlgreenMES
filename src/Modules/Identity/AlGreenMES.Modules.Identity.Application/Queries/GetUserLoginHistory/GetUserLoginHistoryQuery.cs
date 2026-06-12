using AlGreenMES.Modules.Identity.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Queries.GetUserLoginHistory;

public record GetUserLoginHistoryQuery(Guid UserId, int Limit = 20) : IRequest<IReadOnlyList<LoginAttemptDto>>;
