using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.CreateUser;

public class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUser;

    public CreateUserCommandHandler(
        IUserRepository userRepository,
        IIdentityUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUser)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        // SuperAdmin creation rules (revised 15.06.2026):
        //   - Initial SA seeded via DB (no caller exists pre-seed).
        //   - Subsequent SAs are created by other SAs via the API → allowed.
        //   - Non-SA callers cannot create an SA.
        // Combined with peer-protection on Update/Delete/ResetPassword,
        // this gives the "SAs can additively grow but cannot edit/delete
        // each other" property Milos asked for.
        if (request.Role == UserRole.SuperAdmin && !_currentUser.IsInRole("SuperAdmin"))
            throw new ForbiddenException("FORBIDDEN_ROLE_ASSIGNMENT", "Only SuperAdmin can create a SuperAdmin user.");

        var emailExists = await _userRepository.ExistsByEmailAsync(request.Email, request.TenantId, cancellationToken);
        if (emailExists)
            throw new DomainException("USER_EMAIL_EXISTS", $"A user with email '{request.Email}' already exists for this tenant.");

        var passwordHash = _passwordHasher.HashPassword(request.Password);

        var user = User.Create(
            request.TenantId,
            request.Email,
            passwordHash,
            request.FirstName,
            request.LastName,
            request.Role);

        if (request.Role == UserRole.Department && request.ProcessIds is { Count: > 0 })
            user.AssignProcesses(request.TenantId, request.ProcessIds);

        await _userRepository.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return user.Adapt<UserDto>();
    }
}
