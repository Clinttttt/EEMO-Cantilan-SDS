using EEMOCantilanSDS.Application.Command.Rates.SetFacilityRate;
using EEMOCantilanSDS.Application.Dtos.Rates;
using EEMOCantilanSDS.Application.Queries.Rates.GetFacilityRates;
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

    /// <summary>Returns the caller LGU's currently-effective fixed rates (per facility + key).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FacilityRateDto>>> GetRatesAsync()
    {
        var result = await Sender.Send(new GetFacilityRatesQuery());
        return HandleResponse(result);
    }

    /// <summary>Sets/updates one fixed ordinance rate for the caller's municipality (effective today).</summary>
    [HttpPut]
    public async Task<ActionResult<bool>> SetRateAsync([FromBody] SetFacilityRateCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
}
