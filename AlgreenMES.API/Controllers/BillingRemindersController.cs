using AlgreenMES.API.BackgroundServices;
using AlGreenMES.BuildingBlocks.Common.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlgreenMES.API.Controllers;

/// <summary>
/// SA-only manual trigger for the daily billing-reminder scan. Lives in
/// the API host project (not the Tenancy module) because the hosted
/// service it calls is registered here too — no need to introduce an
/// interface in a shared layer just to wire one test endpoint.
///
/// Idempotent per (user, day): calling this twice the same day creates
/// zero additional notifications.
/// </summary>
[ApiController]
[Route("api/tenants/billing-reminders")]
[Authorize(Policy = "RequireSuperAdmin")]
public class BillingRemindersController : ControllerBase
{
    private readonly BillingReminderService _reminderService;

    public BillingRemindersController(BillingReminderService reminderService)
    {
        _reminderService = reminderService;
    }

    [HttpPost("run")]
    [AllowSuperAdminWrite]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        await _reminderService.RunOnceAsync(cancellationToken);
        return Ok(new { triggered = true });
    }
}
