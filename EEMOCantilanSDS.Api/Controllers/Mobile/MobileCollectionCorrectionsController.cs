using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;
[ApiController, Authorize(Roles = "Collector"), Route("api/mobile/collections")]
public sealed class MobileCollectionCorrectionsController(ISender sender, MobileCollectionCorrectionWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("recent")]
    public async Task<ActionResult<MobileRecentCollections>> Recent(CancellationToken ct) => HandleResponse(await workflow.RecentAsync(ct));
    [HttpPost("edit/quote")]
    public async Task<ActionResult<CollectionSessionQuote>> Quote(EditMobileCollectionIntent intent, CancellationToken ct) => HandleResponse(await workflow.QuoteEditAsync(intent, ct));
    [HttpPost("edit")]
    public async Task<ActionResult<MobileCollectionCorrectionResult>> Edit(RecordMobileCollectionEditRequest request, CancellationToken ct) => HandleResponse(await workflow.EditAsync(request, ct));
    [HttpPost("remove")]
    public async Task<ActionResult<MobileCollectionCorrectionResult>> Remove(RemoveMobileCollectionRequest request, CancellationToken ct) => HandleResponse(await workflow.RemoveAsync(request, ct));
}
