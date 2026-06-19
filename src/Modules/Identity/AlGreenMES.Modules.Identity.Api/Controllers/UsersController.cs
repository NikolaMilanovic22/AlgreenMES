using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Authorization;
using AlGreenMES.Modules.Identity.Api.Requests;
using AlGreenMES.Modules.Identity.Application.Commands.ChangePassword;
using AlGreenMES.Modules.Identity.Application.Commands.CreateUser;
using AlGreenMES.Modules.Identity.Application.Commands.DeleteUser;
using AlGreenMES.Modules.Identity.Application.Commands.ResetPassword;
using AlGreenMES.Modules.Identity.Application.Commands.UpdateUser;
using AlGreenMES.Modules.Identity.Application.Queries.GetUserById;
using AlGreenMES.Modules.Identity.Application.Queries.GetUserLoginHistory;
using AlGreenMES.Modules.Identity.Application.Queries.GetUserRoleHistory;
using AlGreenMES.Modules.Identity.Application.Queries.GetSuperAdmins;
using AlGreenMES.Modules.Identity.Application.Queries.GetUsers;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;

    public UsersController(IMediator mediator, ITenantService tenantService)
    {
        _mediator = mediator;
        _tenantService = tenantService;
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> GetUsers(
        [FromQuery] UserRole? role,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetUsersQuery
        {
            TenantId = _tenantService.GetCurrentTenantId(),
            Role = role,
            IsActive = isActive,
            Page = page,
            PageSize = pageSize,
            Search = search,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            SortBy = sortBy,
            SortDirection = sortDirection
        }, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetUserById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUserByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    // SuperAdmin-only listing of every SuperAdmin across every tenant. Feeds
    // the "Sistem administratori" tab on the FE. Returned rows are read-only
    // on the FE — peer-protection guards in Update/Delete/ResetPassword keep
    // it that way even if the FE skips the role check.
    [HttpGet("super-admins")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetSuperAdmins(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetSuperAdminsQuery(), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        // Tenant override (request.TenantId) is allowed only for SuperAdmin
        // — used by the tenant-creation flow to seed the initial Admin in
        // a brand-new tenant. Everyone else creates users in their own
        // home tenant (resolved from the JWT).
        Guid tenantId;
        if (request.TenantId.HasValue && request.TenantId.Value != Guid.Empty)
        {
            if (!User.IsInRole("SuperAdmin"))
                throw new ForbiddenException(
                    "FORBIDDEN_TENANT_OVERRIDE",
                    "Only SuperAdmin can create users in another tenant.");
            tenantId = request.TenantId.Value;
        }
        else
        {
            tenantId = _tenantService.GetCurrentTenantId();
        }

        var result = await _mediator.Send(
            new CreateUserCommand(
                tenantId,
                request.Email,
                request.Password,
                request.FirstName,
                request.LastName,
                request.Role,
                request.ProcessIds),
            cancellationToken);

        return CreatedAtAction(nameof(GetUserById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateUserCommand(id, _tenantService.GetCurrentTenantId(), request.FirstName, request.LastName, request.Role, request.IsActive, request.CanIncludeWithdrawnInAnalysis, request.ProcessIds, request.AdditionalRoles),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Role-change history for one user, newest first. SuperAdmin/Admin only
    /// — viewing audit data is privileged. The list can be empty (user
    /// never had their role changed) and that's a valid response.
    /// </summary>
    [HttpGet("{id:guid}/role-history")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetRoleHistory(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetUserRoleHistoryQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Recent login attempts for one user, newest first. Capped at 100 rows
    /// (handler clamps `limit`) so a misbehaving client can't pull the
    /// whole audit log in one call. Same authz gate as the role history.
    /// </summary>
    [HttpGet("{id:guid}/login-history")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> GetLoginHistory(Guid id, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetUserLoginHistoryQuery(id, limit), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/change-password")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> ChangePassword(Guid id, [FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new ChangePasswordCommand(id, request.CurrentPassword, request.NewPassword),
            cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new ResetPasswordCommand(id, request.NewPassword),
            cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> DeleteUser(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (currentUserId != null && Guid.Parse(currentUserId) == id)
            return BadRequest(new { error = new { code = "CANNOT_DELETE_SELF", message = "You cannot delete your own account" } });

        await _mediator.Send(new DeleteUserCommand(id), cancellationToken);
        return NoContent();
    }
}
