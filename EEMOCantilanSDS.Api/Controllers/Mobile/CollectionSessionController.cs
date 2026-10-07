using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "Collector")]
[ApiController]
[Route("api/mobile/collection-session")]
public sealed class CollectionSessionController(ISender sender, CollectionSessionWorkflow workflow, CollectionComposerWorkflow composer, FishMeatVendorFeeCollectionWorkflow vendorFees) : ApiBaseController(sender)
{
    [HttpGet("sources")]
    public async Task<ActionResult<IReadOnlyList<CollectionSourceSearchResult>>> Sources(string? search, CancellationToken ct) =>
        HandleResponse(await workflow.SearchSourcesAsync(search, ct));
    [HttpPost("source-eligible")]
    public async Task<ActionResult<CollectionSessionDiscovery>> SourceEligible(CollectionSourceIdentity? identity, CancellationToken ct) =>
        HandleResponse(await workflow.DiscoverNativeAsync(identity, ct));
    [HttpGet("payors")]
    public async Task<ActionResult<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Revenue.CollectionPayorDto>>> Payors(string? search, CancellationToken ct) =>
        HandleResponse(await workflow.SearchPayorsAsync(search, ct));
    [HttpGet("electricity-sources")]
    public async Task<ActionResult<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Revenue.EcfObligationQuoteDto>>> ElectricitySources(CancellationToken ct) =>
        HandleResponse(await composer.GetMobileEcfSourcesAsync(ct: ct));
    [HttpGet("eligible")]
    public async Task<ActionResult<CollectionSessionDiscovery>> Eligible(Guid? payorId, CancellationToken ct) =>
        HandleResponse(await workflow.DiscoverAsync(payorId, ct));
    [HttpGet("vendor-fee-sources")]
    public async Task<ActionResult<IReadOnlyList<DirectVendorFeeSource>>> VendorFeeSources(CancellationToken ct) => HandleResponse(await vendorFees.DiscoverAsync(ct: ct));
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
