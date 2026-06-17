using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Tenancy.Api.Requests;
using AlGreenMES.Modules.Tenancy.Application.Commands.BlockTenant;
using AlGreenMES.Modules.Tenancy.Application.Commands.CreateTenant;
using AlGreenMES.Modules.Tenancy.Application.Commands.CreateTenantPayment;
using AlGreenMES.Modules.Tenancy.Application.Commands.DeleteTenantPayment;
using AlGreenMES.Modules.Tenancy.Application.Commands.SetTenantLogo;
using AlGreenMES.Modules.Tenancy.Application.Commands.UnblockTenant;
using AlGreenMES.Modules.Tenancy.Application.Commands.UpdateTenant;
using AlGreenMES.Modules.Tenancy.Application.Commands.UpdateTenantPayment;
using AlGreenMES.Modules.Tenancy.Application.Commands.UpdateTenantSettings;
using AlGreenMES.Modules.Tenancy.Application.Queries.GetTenantById;
using AlGreenMES.Modules.Tenancy.Application.Queries.GetTenantPayments;
using AlGreenMES.Modules.Tenancy.Application.Queries.GetTenants;
using AlGreenMES.Modules.Tenancy.Application.Queries.GetTenantSettings;
using AlGreenMES.Modules.Tenancy.Application.Services;
using AlGreenMES.BuildingBlocks.Common.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Tenancy.Api.Controllers;

// Authorisation is set PER METHOD on this controller because the `me/*`
// endpoints below are open to every tenant member (read settings) and
// the tenant's own Admin (write settings) — those would conflict with
// a class-level `RequireSuperAdmin` policy.
//
// [AllowSuperAdminWrite] is applied PER ACTION too — only on the
// platform-level writes a SuperAdmin owns (tenant CRUD, tenant settings
// addressed by id). The `me/*` writes are tenant-Admin territory and
// must NOT be opted out, so a SuperAdmin browsing a foreign tenant gets
// blocked by SuperAdminReadOnlyMiddleware (the whole point of the
// tenantless model).
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ITenantService _tenantService;
    private readonly ITenantLogoStorage _logoStorage;

    public TenantsController(IMediator mediator, ITenantService tenantService, ITenantLogoStorage logoStorage)
    {
        _mediator = mediator;
        _tenantService = tenantService;
        _logoStorage = logoStorage;
    }

    [HttpGet]
    [Authorize(Policy = "RequireSuperAdmin")]
    public async Task<IActionResult> GetTenants(
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
        var result = await _mediator.Send(new GetTenantsQuery
        {
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
    [Authorize(Policy = "RequireSuperAdmin")]
    public async Task<IActionResult> GetTenantById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "RequireSuperAdmin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateTenantCommand(
            request.Name, request.Code,
            request.DefaultWarningDays, request.DefaultCriticalDays,
            request.WarningColor, request.CriticalColor), cancellationToken);
        return CreatedAtAction(nameof(GetTenantById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireSuperAdmin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> UpdateTenant(Guid id, [FromBody] UpdateTenantRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateTenantCommand(
            id, request.Name,
            request.DefaultWarningDays, request.DefaultCriticalDays,
            request.WarningColor, request.CriticalColor), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}/settings")]
    [Authorize(Policy = "RequireSuperAdmin")]
    public async Task<IActionResult> GetTenantSettings(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantSettingsQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPut("{id:guid}/settings")]
    [Authorize(Policy = "RequireSuperAdmin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> UpdateTenantSettings(Guid id, [FromBody] UpdateTenantSettingsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateTenantSettingsCommand(
            id,
            request.DefaultWarningDays,
            request.DefaultCriticalDays,
            request.WarningColor,
            request.CriticalColor), cancellationToken);

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────
    // "me" endpoints — let the tenant's own Admin read/write their tenant
    // settings without going through the SuperAdmin-gated {id} routes.
    // The current tenant is resolved from the JWT, so the caller can only
    // act on their own tenant.
    //
    // Milos 15.06.2026 — Skysoft (SuperAdmin) creates the tenant + initial
    // Admin; everything else (warning/critical days, theme colors, later
    // logo) is the tenant's own responsibility.
    // ─────────────────────────────────────────────────────────────────────

    [HttpGet("me/settings")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator,SalesManager,Department,Magacioner")]
    public async Task<IActionResult> GetMyTenantSettings(CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.GetCurrentTenantId();
        var result = await _mediator.Send(new GetTenantSettingsQuery(tenantId), cancellationToken);
        return Ok(result);
    }

    [HttpPut("me/settings")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> UpdateMyTenantSettings([FromBody] UpdateTenantSettingsRequest request, CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.GetCurrentTenantId();
        var result = await _mediator.Send(new UpdateTenantSettingsCommand(
            tenantId,
            request.DefaultWarningDays,
            request.DefaultCriticalDays,
            request.WarningColor,
            request.CriticalColor), cancellationToken);
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator,SalesManager,Department,Magacioner")]
    public async Task<IActionResult> GetMyTenant(CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.GetCurrentTenantId();
        var result = await _mediator.Send(new GetTenantByIdQuery(tenantId), cancellationToken);
        return Ok(result);
    }

    // ─── Tenant logo upload ───────────────────────────────────────────────
    // The Upload component on Profil firme posts here. We stream the file
    // to disk via ITenantLogoStorage, persist Tenant.LogoUrl via mediator,
    // and return the updated TenantDto so the FE can swap the sidebar logo
    // immediately. GET is open to any tenant member so the sidebar can
    // render the logo regardless of role.

    [HttpPost("me/logo")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadMyLogo(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = new { code = "LOGO_FILE_REQUIRED", message = "Logo file is required." } });

        if (file.Length > _logoStorage.MaxFileSizeBytes)
            return BadRequest(new { error = new { code = "LOGO_TOO_LARGE", message = $"Max {_logoStorage.MaxFileSizeBytes / 1024 / 1024} MB." } });

        // Validate Content-Type alongside extension — Orders does the same.
        // An attacker can trivially rename script.js to script.png; the MIME
        // check makes them also have to forge the Content-Type header.
        var contentType = file.ContentType ?? string.Empty;
        if (!_logoStorage.AllowedContentTypes.Contains(contentType))
            return BadRequest(new { error = new { code = "LOGO_BAD_CONTENT_TYPE", message = "Allowed: " + string.Join(", ", _logoStorage.AllowedContentTypes) } });

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !_logoStorage.AllowedExtensions.Contains(ext))
            return BadRequest(new { error = new { code = "LOGO_BAD_EXTENSION", message = "Allowed: " + string.Join(", ", _logoStorage.AllowedExtensions) } });

        var tenantId = _tenantService.GetCurrentTenantId();

        await using var stream = file.OpenReadStream();
        var relativePath = await _logoStorage.SaveAsync(tenantId, stream, ext, cancellationToken);

        var result = await _mediator.Send(new SetTenantLogoCommand(tenantId, relativePath), cancellationToken);
        return Ok(result);
    }

    [HttpGet("me/logo")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator,SalesManager,Department,Magacioner")]
    public async Task<IActionResult> GetMyLogo(CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.GetCurrentTenantId();
        var tenant = await _mediator.Send(new GetTenantByIdQuery(tenantId), cancellationToken);
        if (string.IsNullOrWhiteSpace(tenant.LogoUrl))
            return NotFound();

        var file = await _logoStorage.GetAsync(tenant.LogoUrl, cancellationToken);
        if (file is null)
            return NotFound();

        return File(file.Value.Stream, file.Value.ContentType);
    }

    [HttpDelete("me/logo")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<IActionResult> DeleteMyLogo(CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.GetCurrentTenantId();
        var tenant = await _mediator.Send(new GetTenantByIdQuery(tenantId), cancellationToken);
        if (!string.IsNullOrWhiteSpace(tenant.LogoUrl))
            await _logoStorage.DeleteAsync(tenant.LogoUrl, cancellationToken);

        var result = await _mediator.Send(new SetTenantLogoCommand(tenantId, null), cancellationToken);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Naplata (billing) — SuperAdmin-only. Tenant Admins never see these.
    // Payments are free-form date ranges so monthly / quarterly / annual
    // subscriptions all fit. Blocking flips Tenant.IsActive, which the
    // login flow rejects with TENANT_BLOCKED.
    // ─────────────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/payments")]
    [Authorize(Policy = "RequireSuperAdmin")]
    public async Task<IActionResult> GetTenantPayments(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetTenantPaymentsQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/payments")]
    [Authorize(Policy = "RequireSuperAdmin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> CreateTenantPayment(Guid id, [FromBody] CreateTenantPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CreateTenantPaymentCommand(
            id,
            request.PeriodStart,
            request.PeriodEnd,
            request.Amount,
            request.Currency,
            request.PaidAt,
            request.InvoiceNumber,
            request.Notes), cancellationToken);
        return Ok(result);
    }

    [HttpPut("{id:guid}/payments/{paymentId:guid}")]
    [Authorize(Policy = "RequireSuperAdmin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> UpdateTenantPayment(Guid id, Guid paymentId, [FromBody] CreateTenantPaymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UpdateTenantPaymentCommand(
            id,
            paymentId,
            request.PeriodStart,
            request.PeriodEnd,
            request.Amount,
            request.Currency,
            request.PaidAt,
            request.InvoiceNumber,
            request.Notes), cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{id:guid}/payments/{paymentId:guid}")]
    [Authorize(Policy = "RequireSuperAdmin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> DeleteTenantPayment(Guid id, Guid paymentId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteTenantPaymentCommand(id, paymentId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/block")]
    [Authorize(Policy = "RequireSuperAdmin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> BlockTenant(Guid id, [FromBody] BlockTenantRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new BlockTenantCommand(id, request.Reason), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/unblock")]
    [Authorize(Policy = "RequireSuperAdmin")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> UnblockTenant(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new UnblockTenantCommand(id), cancellationToken);
        return Ok(result);
    }
}
