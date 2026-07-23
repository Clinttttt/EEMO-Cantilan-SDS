using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.CreateFirstAdmin;
using EEMOCantilanSDS.Application.Queries.Auth.GetSetupStatus;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EEMOCantilanSDS.Api.Controllers;

[AllowAnonymous]
public class SetupController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("status")]
    public async Task<ActionResult<SetupStatusDto>> GetSetupStatus()
    {
        var result = await Sender.Send(new GetSetupStatusQuery());
        return HandleResponse(result);
    }

    // Anonymous first-run bootstrap: self-disables once an admin exists. Rate-limited + antiforgery-exempt
    // for parity with PlatformSetupController.CreateFirstOperator (both are unauthenticated setup POSTs).
    [HttpPost("create-first-admin")]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<bool>> CreateFirstAdmin([FromBody] CreateFirstAdminCommand request)
    {
        var result = await Sender.Send(request);
        return HandleResponse(result);
    }
}


