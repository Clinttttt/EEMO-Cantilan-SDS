using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Queries.Collections.GetCollectionActivity;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/collections/activity")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class CollectionActivityController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>
    /// Unified office Collection Activity for an inclusive business-date period (at most 31 days): authoritative legacy and
    /// canonical collection events, each real collection exactly once, with line detail and linked corrections.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<CollectionActivityFeedDto>> Get(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] FacilityCode? facility, [FromQuery] Guid? collectorId, [FromQuery] string? authority,
        [FromQuery] int limit = GetCollectionActivityQuery.DefaultLimit)
        => HandleResponse(await Sender.Send(new GetCollectionActivityQuery(from, to, facility, collectorId, authority, limit)));
}
