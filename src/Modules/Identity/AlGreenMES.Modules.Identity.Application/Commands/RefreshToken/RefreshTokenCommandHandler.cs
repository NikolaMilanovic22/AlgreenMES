using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.RefreshToken;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, LoginResponseDto>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly IJwtTokenService _jwtTokenService;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        IUserRepository userRepository,
        IIdentityUnitOfWork unitOfWork,
        IJwtTokenService jwtTokenService)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<LoginResponseDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var existingToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken, cancellationToken)
            ?? throw new DomainException("INVALID_REFRESH_TOKEN", "The refresh token is invalid.");

        if (!existingToken.IsValid())
            throw new DomainException("INVALID_REFRESH_TOKEN", "The refresh token is invalid or expired.");

        // Refresh runs pre-auth (access token expired) — bypass HasQueryFilter and validate tenant explicitly.
        var user = await _userRepository.GetByIdIgnoreFiltersAsync(existingToken.UserId, cancellationToken)
            ?? throw new DomainException("INVALID_REFRESH_TOKEN", "The refresh token is invalid.");

        if (!user.IsActive)
            throw new DomainException("USER_INACTIVE", "The user account is not active.");

        if (user.TenantId != existingToken.TenantId)
        {
            // Defense in depth — refresh tokens are tenant-scoped; a mismatch means tampering or data corruption.
            throw new DomainException("INVALID_REFRESH_TOKEN", "The refresh token is invalid.");
        }

        existingToken.Revoke();

        var newToken = _jwtTokenService.GenerateToken(user);
        var newRefreshTokenValue = _jwtTokenService.GenerateRefreshToken();

        var newRefreshToken = Domain.Entities.RefreshToken.Create(
            existingToken.TenantId,
            user.Id,
            newRefreshTokenValue,
            DateTime.UtcNow.AddDays(7));

        await _refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var userDto = user.Adapt<UserDto>();

        return new LoginResponseDto(newToken, newRefreshTokenValue, userDto);
    }
}
