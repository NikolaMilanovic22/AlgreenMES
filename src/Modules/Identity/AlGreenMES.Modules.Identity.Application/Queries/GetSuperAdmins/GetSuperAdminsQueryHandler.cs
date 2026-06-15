using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Queries.GetSuperAdmins;

public class GetSuperAdminsQueryHandler : IRequestHandler<GetSuperAdminsQuery, IReadOnlyList<UserDto>>
{
    private readonly IUserRepository _userRepository;

    public GetSuperAdminsQueryHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<UserDto>> Handle(GetSuperAdminsQuery request, CancellationToken cancellationToken)
    {
        var users = await _userRepository.GetAllSuperAdminsAsync(cancellationToken);
        return users.Select(u => u.Adapt<UserDto>()).ToList();
    }
}
