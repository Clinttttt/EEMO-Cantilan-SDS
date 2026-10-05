using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "Collector")]
[ApiController]
[Route("api/mobile/collection-session")]
public sealed class CollectionSessionController(ISender sender, CollectionSessionWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("eligible")]
    public async Task<ActionResult<CollectionSessionDiscovery>> Eligible(Guid? payorId, CancellationToken ct) =>
        HandleResponse(await workflow.DiscoverAsync(payorId, ct));
    [HttpPost("quote")]
    public async Task<ActionResult<CollectionSessionQuote>> Quote(CollectionSessionIntent intent, CancellationToken ct) =>
        HandleResponse(await workflow.QuoteAsync(intent, ct));
    [HttpPost("record")]
    public async Task<ActionResult<CollectionSessionResult>> Record(RecordCollectionSessionRequest request, CancellationToken ct)
    {
        var result = await workflow.RecordAsync(request, ct);
        if (result.IsSuccess && result.Value!.Problems.Any(p => p.Code == "SessionIntentConflict")) return Conflict(result.Value);
        return HandleResponse(result);
    }
    [HttpGet("{clientSessionId:guid}")]
    public async Task<ActionResult<CollectionSessionResult>> Get(Guid clientSessionId, CancellationToken ct) =>
        HandleResponse(await workflow.GetAsync(clientSessionId, ct));
}
