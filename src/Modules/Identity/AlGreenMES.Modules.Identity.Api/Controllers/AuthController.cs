using AlGreenMES.Modules.Identity.Api.Requests;
using AlGreenMES.Modules.Identity.Application.Commands.Login;
using AlGreenMES.Modules.Identity.Application.Commands.RefreshToken;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AlGreenMES.Modules.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
// Login and refresh are by definition pre-auth — there's no JWT to
// validate yet. Explicit [AllowAnonymous] both documents that and
// satisfies the pre-commit "no controller without an auth declaration"
// check (so a future authenticated endpoint added here doesn't quietly
// become public).
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // 10 attempts per client IP per 5 min. Returns 429 once exceeded so the
    // FE can show "too many attempts" instead of "wrong password" forever.
    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        // Capture the originating IP + UA for the login-attempt audit log.
        // Behind nginx the real client IP lives in X-Forwarded-For; fall
        // back to the socket address for direct calls (tests, internal).
        var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
            ?? HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        var result = await _mediator.Send(
            new LoginCommand(request.Email, request.Password, request.TenantCode, ip, userAgent),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RefreshTokenCommand(request.RefreshToken),
            cancellationToken);

        return Ok(result);
    }
}
