using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Auth.CreateFirstConsoleAdmin;
using EEMOCantilanSDS.Application.Queries.Auth.GetPlatformSetupStatus;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// First-run bootstrap for the platform/console operator (onboarding manager). Anonymous by design — like
/// the main SetupController — but creation self-disables once an operator exists, so it cannot become a
/// public back door.
/// </summary>
[AllowAnonymous]
[Route("api/platform-setup")]
[ApiController]
public class PlatformSetupController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Whether the console still needs its first operator account.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<PlatformSetupStatusDto>> GetStatus()
    {
        var result = await Sender.Send(new GetPlatformSetupStatusQuery());
        return HandleResponse(result);
    }

    /// <summary>Create the first console operator (only when none exists). Rate-limited.</summary>
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("auth")]
    [HttpPost("create-first-operator")]
    public async Task<ActionResult<bool>> CreateFirstOperator([FromBody] CreateFirstConsoleAdminCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
}
