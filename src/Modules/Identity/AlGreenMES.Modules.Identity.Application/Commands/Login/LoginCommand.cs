using AlGreenMES.Modules.Identity.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.Login;

public record LoginCommand(
    string Email,
    string Password,
    string TenantCode,
    string? IpAddress = null,
    string? UserAgent = null) : IRequest<LoginResponseDto>;
