using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Identity.Domain.Entities;
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

        // Defense in depth: a refresh token is tenant-scoped. For a normal
        // user, the refresh token's tenant MUST equal the user's home
        // tenant — anything else means tampering. SuperAdmins (Milos
        // 16.06.2026) are tenantless (user.TenantId is null) and their
        // refresh tokens carry whatever tenant they were browsing at
        // login, so we only enforce the equality for non-SAs.
        if (user.Role != UserRole.SuperAdmin && user.TenantId != existingToken.TenantId)
        {
            throw new DomainException("INVALID_REFRESH_TOKEN", "The refresh token is invalid.");
        }

        existingToken.Revoke();

        // The new JWT reuses the tenant the refresh token was issued for —
        // preserves the session's scope across refresh whether normal or SA.
        var effectiveTenantId = existingToken.TenantIdRequired;
        var newToken = _jwtTokenService.GenerateToken(user, effectiveTenantId);
        var newRefreshTokenValue = _jwtTokenService.GenerateRefreshToken();

        var newRefreshToken = Domain.Entities.RefreshToken.Create(
            effectiveTenantId,
            user.Id,
            newRefreshTokenValue,
            DateTime.UtcNow.AddDays(7));

        await _refreshTokenRepository.AddAsync(newRefreshToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var userDto = user.Adapt<UserDto>();

        return new LoginResponseDto(newToken, newRefreshTokenValue, userDto);
    }
}
