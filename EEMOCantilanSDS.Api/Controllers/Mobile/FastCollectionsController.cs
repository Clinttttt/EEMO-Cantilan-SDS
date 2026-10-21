using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
namespace EEMOCantilanSDS.Api.Controllers;

[ApiController]
[Authorize(Roles = "Collector")]
[Route("api/mobile/fast-collections")]
public sealed class FastCollectionsController(ISender sender, FeeScheduleCollectionWorkflow schedule,
    CollectionComposerWorkflow composer) : ApiBaseController(sender)
{
    [HttpPost("tabo/quote")]
    public async Task<ActionResult<TaboBatchQuoteDto>> QuoteAsync(TaboBatchRequest request, CancellationToken ct) =>
        HandleResponse(await schedule.QuoteTaboBatchAsync(request, ct));
    [HttpPost("tabo/record")]
    public async Task<ActionResult<TaboBatchOutcomeDto>> RecordAsync(TaboBatchRequest request, CancellationToken ct) =>
        HandleResponse(await schedule.RecordTaboBatchAsync(request, ct));
    /// <summary>The approved Slaughterhouse charges (with today's fixed rates) a facility-assigned collector may select, and whether the service is itemized.</summary>
    [HttpGet("slaughter/terms")]
    public async Task<ActionResult<SlaughterTermsDto>> SlaughterTermsAsync([FromQuery] DateOnly? date, CancellationToken ct) =>
        HandleResponse(await schedule.GetSlaughterTermsAsync(date, ct));
    [HttpPost("slaughter/quote")]
    public async Task<ActionResult<FeeScheduleCollectionWorkflow.SlaughterQuote>> SlaughterQuoteAsync(FeeSchedulePostRequest request, CancellationToken ct) =>
        HandleResponse(await schedule.QuoteSlaughterAsync(request, ct));
    [HttpPost("space-obligation")]
    public async Task<ActionResult<EcfPostOutcomeDto>> SpaceAsync(MobileObligationPostRequest request, CancellationToken ct) =>
        HandleResponse(await composer.PostMobileObligationAsync(request, ct));
}
