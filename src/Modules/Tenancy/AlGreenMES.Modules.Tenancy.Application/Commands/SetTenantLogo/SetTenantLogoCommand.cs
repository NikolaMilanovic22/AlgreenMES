using AlGreenMES.Modules.Tenancy.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.SetTenantLogo;

/// <summary>
/// Persist (or clear) the LogoUrl of the given tenant after the controller
/// has already saved the file via <see cref="Services.ITenantLogoStorage"/>.
/// Pass <c>null</c> for <paramref name="LogoUrl"/> when removing the logo.
/// </summary>
public record SetTenantLogoCommand(Guid TenantId, string? LogoUrl) : IRequest<TenantDto>;
