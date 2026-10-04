using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/ecf-collections")]
[ApiController]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class EcfCollectionsController(ISender sender, EcfCollectionWorkflow workflow)
    : ApiBaseController(sender)
{
    [HttpGet("obligations")]
    public async Task<ActionResult<IReadOnlyList<EcfObligationQuoteDto>>> GetObligations(
        [FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        HandleResponse(await workflow.GetObligationsAsync(year, month, ct));

    [HttpGet("obligations/{utilityBillId:guid}")]
    public async Task<ActionResult<EcfObligationQuoteDto>> GetObligation(Guid utilityBillId, CancellationToken ct) =>
        HandleResponse(await workflow.GetObligationAsync(utilityBillId, ct));

    [HttpGet("drafts/current")]
    public async Task<ActionResult<EcfCollectionDraftDto>> GetCurrentDraft(CancellationToken ct) =>
        HandleResponse(await workflow.GetCurrentDraftAsync(ct));

    [HttpGet("drafts/{draftId:guid}")]
    public async Task<ActionResult<EcfCollectionDraftDto>> GetDraft(Guid draftId, CancellationToken ct) =>
        HandleResponse(await workflow.GetDraftAsync(draftId, ct));

    [HttpPost("drafts")]
    public async Task<ActionResult<EcfCollectionDraftDto>> CreateDraft(
        [FromBody] CreateEcfCollectionDraftRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.CreateDraftAsync(request, ct));

    [HttpPut("drafts/{draftId:guid}/allocation")]
    public async Task<ActionResult<EcfCollectionDraftDto>> UpdateAllocation(
        Guid draftId, [FromBody] UpdateEcfDraftAllocationRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.UpdateAllocationAsync(draftId, request, ct));

    [HttpPost("drafts/{draftId:guid}/review")]
    public async Task<ActionResult<EcfCollectionDraftDto>> Review(
        Guid draftId, [FromBody] EcfDraftRevisionRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.ReviewAsync(draftId, request, ct));

    [HttpPost("drafts/{draftId:guid}/discard")]
    public async Task<ActionResult<EcfCollectionDraftDto>> Discard(
        Guid draftId, [FromBody] EcfDraftRevisionRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.DiscardAsync(draftId, request, ct));

    [HttpPost("drafts/{draftId:guid}/post")]
    public async Task<ActionResult<EcfPostOutcomeDto>> Post(
        Guid draftId, [FromBody] PostEcfCollectionDraftRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.PostAsync(draftId, request, ct));

    [HttpGet("activity")]
    public async Task<ActionResult<IReadOnlyList<EcfCollectionActivityDto>>> GetActivity(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        HandleResponse(await workflow.GetActivityAsync(from, to, ct));
}
