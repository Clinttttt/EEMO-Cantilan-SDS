using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
}
