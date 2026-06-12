using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Application.Services;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using Mapster;
using RefreshTokenEntity = AlGreenMES.Modules.Identity.Domain.Entities.RefreshToken;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponseDto>
{
    // Account lockout policy. Tuned for a B2B internal app with manager-set
    // passwords — 5 tries / 15 min is generous enough that a worker who
    // mistypes their password won't get locked out on their second wrong
    // attempt, but tight enough that an unattended password-guess script
    // gets shut down quickly.
    private const int LockoutThreshold = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ILoginAttemptRepository _loginAttemptRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ITenantLookupService _tenantLookupService;

    public LoginCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        ILoginAttemptRepository loginAttemptRepository,
        IIdentityUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ITenantLookupService tenantLookupService)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _loginAttemptRepository = loginAttemptRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _tenantLookupService = tenantLookupService;
    }

    public async Task<LoginResponseDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var emailNormalized = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        // ──────────────────────────────────────────────────────────────
        // Stage 1: resolve the tenant. Failures here can't blame a user;
        // we still log the attempt with TenantId=null so audit can see
        // "someone keeps hitting tenant code 'XYZ' that doesn't exist".
        // ──────────────────────────────────────────────────────────────
        var tenant = await _tenantLookupService.GetTenantByCodeAsync(request.TenantCode, cancellationToken);
        if (tenant == null)
        {
            await LogAndSaveAsync(LoginAttempt.RecordFailure(null, emailNormalized, "TENANT_NOT_FOUND", request.IpAddress, request.UserAgent, now), cancellationToken);
            throw new NotFoundException("Tenant", request.TenantCode);
        }
        if (!tenant.IsActive)
        {
            await LogAndSaveAsync(LoginAttempt.RecordFailure(tenant.Id, emailNormalized, "TENANT_INACTIVE", request.IpAddress, request.UserAgent, now), cancellationToken);
            throw new DomainException("TENANT_INACTIVE", "The tenant is not active.");
        }

        // ──────────────────────────────────────────────────────────────
        // Stage 2: resolve the user. If the email doesn't match, we still
        // log with TenantId set so an admin can later see "this tenant got
        // hit with these unknown emails".
        // ──────────────────────────────────────────────────────────────
        var user = await _userRepository.GetByEmailAsync(emailNormalized, tenant.Id, cancellationToken);
        if (user == null)
        {
            await LogAndSaveAsync(LoginAttempt.RecordFailure(tenant.Id, emailNormalized, "INVALID_CREDENTIALS", request.IpAddress, request.UserAgent, now), cancellationToken);
            throw new DomainException("INVALID_CREDENTIALS", "Invalid email or password.");
        }
        if (!user.IsActive)
        {
            await LogAndSaveAsync(LoginAttempt.RecordFailure(tenant.Id, emailNormalized, "USER_INACTIVE", request.IpAddress, request.UserAgent, now), cancellationToken);
            throw new DomainException("USER_INACTIVE", "The user account is not active.");
        }

        // ──────────────────────────────────────────────────────────────
        // Stage 3: lockout check before password verify, so a locked
        // account never burns CPU on bcrypt for the attacker.
        // ──────────────────────────────────────────────────────────────
        if (user.IsLockedOut(now))
        {
            await LogAndSaveAsync(LoginAttempt.RecordFailure(tenant.Id, emailNormalized, "ACCOUNT_LOCKED", request.IpAddress, request.UserAgent, now), cancellationToken);
            throw new DomainException("ACCOUNT_LOCKED", "Account is temporarily locked due to too many failed attempts. Try again later.");
        }

        // ──────────────────────────────────────────────────────────────
        // Stage 4: password compare. On failure, count the attempt; on
        // success, reset the counter.
        // ──────────────────────────────────────────────────────────────
        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            user.RegisterFailedLogin(now, LockoutThreshold, LockoutDuration);
            await _loginAttemptRepository.AddAsync(
                LoginAttempt.RecordFailure(tenant.Id, emailNormalized, "INVALID_CREDENTIALS", request.IpAddress, request.UserAgent, now),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainException("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        user.RegisterSuccessfulLogin();

        var token = _jwtTokenService.GenerateToken(user);
        var refreshTokenValue = _jwtTokenService.GenerateRefreshToken();

        var refreshToken = RefreshTokenEntity.Create(
            tenant.Id,
            user.Id,
            refreshTokenValue,
            now.AddDays(7));

        await _refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await _loginAttemptRepository.AddAsync(
            LoginAttempt.RecordSuccess(tenant.Id, emailNormalized, request.IpAddress, request.UserAgent, now),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var userDto = user.Adapt<UserDto>();
        return new LoginResponseDto(token, refreshTokenValue, userDto);
    }

    /// <summary>
    /// Add the attempt + flush in one shot. Used on the early-exit paths
    /// (tenant lookup failure, user lookup failure, account lockout)
    /// where no other state mutates so saving immediately is fine.
    /// </summary>
    private async Task LogAndSaveAsync(LoginAttempt attempt, CancellationToken cancellationToken)
    {
        await _loginAttemptRepository.AddAsync(attempt, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
