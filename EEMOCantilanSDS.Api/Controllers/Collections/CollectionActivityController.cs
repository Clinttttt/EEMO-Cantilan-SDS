using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Queries.Collections.GetCollectionActivity;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/collections/activity")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class CollectionActivityController(ISender sender, HeadCollectionCorrectionWorkflow corrections) : ApiBaseController(sender)
{
    /// <summary>Review facts and correction eligibility for one SRC. Read-only; any Office user.</summary>
    [HttpGet("{collectionId:guid}/review")]
    public async Task<ActionResult<CollectionReviewDto>> Review(Guid collectionId, CancellationToken ct)
        => HandleResponse(await corrections.ReviewAsync(collectionId, ct));

    /// <summary>Head-only reversal of one eligible canonical rent receipt. SuperAdmin is enforced here and again in the workflow.</summary>
    [HttpPost("reversals")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<HeadCorrectionResultDto>> Reverse([FromBody] HeadReverseCollectionRequest request, CancellationToken ct)
        => HandleResponse(await corrections.ReverseAsync(request, ct));

    [HttpGet("reconciliation")]
    public async Task<ActionResult<CollectionReconciliationDto>> Reconciliation([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] CollectionReconciliationBasis basis, [FromQuery] DateTimeOffset? asOf, [FromQuery] int limit = 500,
        CancellationToken ct = default) => HandleResponse(await Sender.Send(new GetCollectionReconciliationQuery(from, to, basis, asOf, limit), ct));
    /// <summary>
    /// Unified office Collection Activity for an inclusive business-date period (at most 31 days): authoritative legacy and
    /// canonical collection events, each real collection exactly once, with line detail and linked corrections.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<CollectionActivityFeedDto>> Get(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] FacilityCode? facility, [FromQuery] Guid? collectorId, [FromQuery] string? authority,
        [FromQuery] int limit = GetCollectionActivityQuery.DefaultLimit, [FromQuery] string? reference = null)
        => HandleResponse(await Sender.Send(new GetCollectionActivityQuery(from, to, facility, collectorId, authority, limit, reference)));
}
