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
    [HttpPost("space-obligation")]
    public async Task<ActionResult<EcfPostOutcomeDto>> SpaceAsync(MobileObligationPostRequest request, CancellationToken ct) =>
        HandleResponse(await composer.PostMobileObligationAsync(request, ct));
}
