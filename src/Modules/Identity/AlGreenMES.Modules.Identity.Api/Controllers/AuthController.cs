using AlGreenMES.Modules.Identity.Api.Requests;
using AlGreenMES.Modules.Identity.Application.Commands.Login;
using AlGreenMES.Modules.Identity.Application.Commands.RefreshToken;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AlGreenMES.Modules.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
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
        var result = await _mediator.Send(
            new LoginCommand(request.Email, request.Password, request.TenantCode),
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
