using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Queries.GetUserRoleHistory;

public class GetUserRoleHistoryQueryHandler : IRequestHandler<GetUserRoleHistoryQuery, IReadOnlyList<UserRoleChangeEntryDto>>
{
    private readonly IUserRoleChangeLogRepository _repository;

    public GetUserRoleHistoryQueryHandler(IUserRoleChangeLogRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<UserRoleChangeEntryDto>> Handle(GetUserRoleHistoryQuery request, CancellationToken cancellationToken)
    {
        var rows = await _repository.GetForUserWithActorAsync(request.UserId, cancellationToken);
        return rows
            .Select(r => new UserRoleChangeEntryDto(
                r.Log.Id,
                r.Log.OldRole.ToString(),
                r.Log.NewRole.ToString(),
                r.Log.ChangedByUserId,
                r.ChangedByUserFullName,
                r.Log.ChangedAt,
                r.Log.Reason))
            .ToList();
    }
}
