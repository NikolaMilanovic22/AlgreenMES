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
        // SuperAdmin is platform-level and only granted via direct DB
        // insert (Milos 12.06.2026). Reject all API attempts to create a
        // SuperAdmin user, including when the caller is themselves a
        // SuperAdmin — initial seeding goes through SQL, not this path.
        if (request.Role == UserRole.SuperAdmin)
            throw new DomainException("FORBIDDEN_ROLE_ASSIGNMENT", "The SuperAdmin role can only be granted directly in the database.");

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
