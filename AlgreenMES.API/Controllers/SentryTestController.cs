using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlgreenMES.API.Controllers;

/// <summary>
/// TEMPORARY — used once to verify Sentry capture works on a fresh deploy.
/// Remove this file in a follow-up commit before merging to master.
/// </summary>
[ApiController]
[Route("api/_sentry-test")]
[AllowAnonymous]
public class SentryTestController : ControllerBase
{
    [HttpGet]
    public IActionResult ThrowTest()
    {
        throw new Exception("Sentry test — remove this endpoint after verifying.");
    }
}
