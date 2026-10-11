using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EEMOCantilanSDS.Api.Controllers;

[ApiController, Route("api/fine-settlements"), Authorize(Roles = "SuperAdmin,Admin,Collector")]
public sealed class FineSettlementsController(ISender sender, CollectionComposerWorkflow composer) : ApiBaseController(sender)
{
    [HttpGet("{id:guid}/quote")] public async Task<ActionResult<FineSettlementQuoteDto>> Quote(Guid id, CancellationToken ct)
        => HandleResponse(await composer.QuoteFineAsync(id, ct));
    [HttpGet("{id:guid}/history")] public async Task<ActionResult<IReadOnlyList<FineSettlementHistoryDto>>> History(Guid id,CancellationToken ct)
        => HandleResponse(await composer.FineHistoryAsync(id,ct));
    [HttpGet("{id:guid}/current-draft")] public async Task<ActionResult<EcfCollectionDraftDto>> Current(Guid id, CancellationToken ct)
        => HandleResponse(await composer.CurrentFineDraftAsync(id, ct));
    [HttpPost("drafts")] public async Task<ActionResult<EcfCollectionDraftDto>> Add(AddFineAssessmentDraftRequest request, CancellationToken ct)
        => HandleResponse(await composer.AddFineAssessmentAsync(request, ct));
    [HttpGet("drafts/{id:guid}")] public async Task<ActionResult<EcfCollectionDraftDto>> Draft(Guid id, CancellationToken ct)
        => HandleResponse(await composer.GetFineDraftAsync(id, ct));
    [HttpPost("drafts/{id:guid}/review")] public async Task<ActionResult<EcfCollectionDraftDto>> Review(Guid id, EcfDraftRevisionRequest request, CancellationToken ct)
        => HandleResponse(await composer.ReviewAsync(id, request, ct));
    [HttpPost("drafts/{id:guid}/discard")] public async Task<ActionResult<EcfCollectionDraftDto>> Discard(Guid id,EcfDraftRevisionRequest request,CancellationToken ct)
        => HandleResponse(await composer.DiscardAsync(id,request,ct));
    [HttpPost("drafts/{id:guid}/post")] public async Task<ActionResult<EcfPostOutcomeDto>> Post(Guid id, PostEcfCollectionDraftRequest request, CancellationToken ct)
        => HandleResponse(await composer.PostAsync(id, request, ct));
}
