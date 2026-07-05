using EEMOCantilanSDS.Application.Command.Rates.SetFacilityRate;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Self-service fee configuration for an LGU (post-activation). Restricted to the Head (SuperAdmin); the
/// change is scoped to the caller's own municipality and takes effect today forward.
/// </summary>
[Authorize(Roles = "SuperAdmin")]
[Route("api/facility-rates")]
[ApiController]
public class FacilityRatesController : ApiBaseController
{
    public FacilityRatesController(ISender sender) : base(sender)
    {
    }

    /// <summary>Sets/updates one fixed ordinance rate for the caller's municipality (effective today).</summary>
    [HttpPut]
    public async Task<ActionResult<bool>> SetRateAsync([FromBody] SetFacilityRateCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
}
