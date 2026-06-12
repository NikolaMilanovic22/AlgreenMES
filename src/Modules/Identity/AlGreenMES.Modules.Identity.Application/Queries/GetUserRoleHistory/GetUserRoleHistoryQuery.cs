using AlGreenMES.Modules.Identity.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Queries.GetUserRoleHistory;

public record GetUserRoleHistoryQuery(Guid UserId) : IRequest<IReadOnlyList<UserRoleChangeEntryDto>>;
