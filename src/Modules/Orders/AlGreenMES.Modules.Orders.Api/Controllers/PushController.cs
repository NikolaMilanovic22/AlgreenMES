using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PushController : ControllerBase
{
    private readonly IWebPushService _webPushService;
    private readonly ITenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;

    public PushController(
        IWebPushService webPushService,
        ITenantService tenantService,
        ICurrentUserService currentUserService)
    {
        _webPushService = webPushService;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
    }

    [HttpGet("vapid-public-key")]
    public IActionResult GetVapidPublicKey()
    {
        return Ok(new { publicKey = _webPushService.GetVapidPublicKey() });
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request, CancellationToken cancellationToken)
    {
        // The subscription owner is ALWAYS the authenticated caller — never the
        // client-supplied request.UserId. Trusting the body let any logged-in
        // user register their browser endpoint under another user's id and then
        // receive that user's push payloads (order/block content). The DTO field
        // is kept for FE compatibility but ignored server-side.
        await _webPushService.SubscribeAsync(
            _tenantService.GetCurrentTenantId(),
            _currentUserService.GetCurrentUserId(),
            request.Endpoint,
            request.P256dhKey,
            request.AuthKey,
            cancellationToken);

        return StatusCode(201);
    }

    [HttpDelete("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromQuery] string endpoint, CancellationToken cancellationToken)
    {
        await _webPushService.UnsubscribeAsync(_currentUserService.GetCurrentUserId(), endpoint, cancellationToken);
        return NoContent();
    }
}

public record SubscribeRequest(
    Guid UserId,
    string Endpoint,
    string P256dhKey,
    string AuthKey);
