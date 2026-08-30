using EEMOCantilanSDS.Application.Command.OrSeries.AdvanceOrSeries;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Application.Queries.OrSeries.GetOrSeriesSuggestion;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Per-LGU Official Receipt (OR) series suggestions. OR numbers stay manually entered by admins — these
/// endpoints only surface a suggested next value (scoped to the caller's municipality) that the portal
/// pre-fills; the admin confirms or overrides it, and <c>advance</c> is called only when the suggestion
/// was actually used.
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
[Route("api/or-series")]
[ApiController]
public class OrSeriesController : ApiBaseController
{
    public OrSeriesController(ISender sender) : base(sender)
    {
    }

    /// <summary>Returns the suggested next OR number for the caller's LGU (non-consuming).</summary>
    [HttpGet("next")]
    public async Task<ActionResult<OrSeriesSuggestionDto>> GetNext()
    {
        var result = await Sender.Send(new GetOrSeriesSuggestionQuery());
        return HandleResponse(result);
    }

    /// <summary>Advances the counter after a receipt used the suggestion; returns the new suggestion.</summary>
    [HttpPost("advance")]
    public async Task<ActionResult<string>> Advance()
    {
        var result = await Sender.Send(new AdvanceOrSeriesCommand());
        return HandleResponse(result);
    }
}
