using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Application.Command.Onboarding.SetAdminPasswordByToken;
using EEMOCantilanSDS.Application.Queries.Onboarding.GetActivationContext;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Platform-operator onboarding endpoints (Phase 6). Activation commits a staged LGU configuration into the
/// shared database under its own MunicipalityId and provisions its Head. Restricted to a SuperAdmin; the
/// handler additionally verifies the caller is the DEFAULT (Cantilan) municipality's SuperAdmin — the
/// platform operator — so a per-LGU Head can never provision another municipality.
/// </summary>
[Authorize(Roles = "SuperAdmin")]
[Route("api/activation")]
[ApiController]
public class ActivationController : ApiBaseController
{
    public ActivationController(ISender sender) : base(sender)
    {
    }

    /// <summary>Commits an onboarding configuration and takes the target municipality live.</summary>
    [HttpPost("municipality")]
    public async Task<ActionResult<ActivationResultDto>> ActivateMunicipalityAsync([FromBody] ActivateMunicipalityCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    /// <summary>
    /// Completes a provisioned Head's activation via their one-time link token: sets their password and
    /// activates the account. Anonymous (the token is the credential) and rate-limited.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("set-password")]
    public async Task<ActionResult<bool>> SetPasswordAsync([FromBody] SetAdminPasswordByTokenCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    /// <summary>
    /// Resolves the display context (full name, username, office) for a one-time activation token so the
    /// set-password page can confirm the Head's identity. Anonymous (the token is the credential) and
    /// rate-limited; returns a generic failure for any invalid/expired token.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpGet("context/{token}")]
    public async Task<ActionResult<ActivationContextDto>> GetContextAsync(string token)
    {
        var result = await Sender.Send(new GetActivationContextQuery(token));
        return HandleResponse(result);
    }
}
