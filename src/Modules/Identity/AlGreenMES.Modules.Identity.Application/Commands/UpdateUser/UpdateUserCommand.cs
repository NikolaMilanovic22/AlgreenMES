using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Domain.Entities;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.UpdateUser;

public record UpdateUserCommand(
    Guid Id,
    Guid TenantId,
    string FirstName,
    string LastName,
    UserRole Role,
    bool IsActive,
    bool CanIncludeWithdrawnInAnalysis,
    List<Guid>? ProcessIds,
    /// <summary>Extra roles beyond the primary <see cref="Role"/>. Saša
    /// 08.06.2026 — a user can be e.g. Coordinator + Magacioner. Null
    /// means "don't touch existing additional roles"; an empty list
    /// means "clear them all".</summary>
    List<UserRole>? AdditionalRoles) : IRequest<UserDto>;
