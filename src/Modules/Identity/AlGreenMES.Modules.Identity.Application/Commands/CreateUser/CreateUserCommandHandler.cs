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
        // SuperAdmin creation rules (16.06.2026 — tenantless SA):
        //   - Initial SA seeded via DB (no caller exists pre-seed).
        //   - Subsequent SAs are created by other SAs via the API → allowed.
        //   - Non-SA callers cannot create an SA.
        //   - The created SA carries TenantId = null; the request's
        //     TenantId is ignored on this path so we don't accidentally
        //     pin them to whichever tenant the caller is browsing.
        if (request.Role == UserRole.SuperAdmin && !_currentUser.IsInRole("SuperAdmin"))
            throw new ForbiddenException("FORBIDDEN_ROLE_ASSIGNMENT", "Only SuperAdmin can create a SuperAdmin user.");

        var passwordHash = _passwordHasher.HashPassword(request.Password);

        User user;
        if (request.Role == UserRole.SuperAdmin)
        {
            // Email uniqueness for SAs is platform-wide (no tenant to scope).
            var existing = await _userRepository.GetByEmailAcrossTenantsAsync(request.Email, cancellationToken);
            if (existing is not null)
                throw new DomainException("USER_EMAIL_EXISTS", $"A user with email '{request.Email}' already exists.");

            user = User.CreateSuperAdmin(request.Email, passwordHash, request.FirstName, request.LastName);
        }
        else
        {
            var emailExists = await _userRepository.ExistsByEmailAsync(request.Email, request.TenantId, cancellationToken);
            if (emailExists)
                throw new DomainException("USER_EMAIL_EXISTS", $"A user with email '{request.Email}' already exists for this tenant.");

            user = User.Create(
                request.TenantId,
                request.Email,
                passwordHash,
                request.FirstName,
                request.LastName,
                request.Role);

            if (request.Role == UserRole.Department && request.ProcessIds is { Count: > 0 })
                user.AssignProcesses(request.TenantId, request.ProcessIds);
        }

        await _userRepository.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return user.Adapt<UserDto>();
    }
}
