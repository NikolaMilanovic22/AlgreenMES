using AlGreenMES.Modules.Identity.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Queries.GetSuperAdmins;

/// <summary>
/// Lists every SuperAdmin user across every tenant. Read-only for the
/// "Sistem administratori" tab in the UI. SuperAdmin-only at the controller.
/// </summary>
public record GetSuperAdminsQuery() : IRequest<IReadOnlyList<UserDto>>;
