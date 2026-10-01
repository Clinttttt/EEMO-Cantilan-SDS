using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/collections/composer")]
[ApiController]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class CollectionComposerController(ISender sender, CollectionComposerWorkflow composer)
    : ApiBaseController(sender)
{
    [HttpGet("drafts/current")]
    public async Task<ActionResult<EcfCollectionDraftDto>> GetCurrentDraft(CancellationToken ct) =>
        HandleResponse(await composer.GetCurrentDraftAsync(ct));

    [HttpGet("drafts/{draftId:guid}")]
    public async Task<ActionResult<EcfCollectionDraftDto>> GetDraft(Guid draftId, CancellationToken ct) =>
        HandleResponse(await composer.GetDraftAsync(draftId, ct));

    [HttpGet("rent-obligation")]
    public async Task<ActionResult<RentObligationQuoteDto>> GetRentObligation(
        [FromQuery] Guid stallId, [FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        HandleResponse(await composer.GetRentObligationAsync(stallId, year, month, ct));

    [HttpGet("payors/{payorId:guid}/obligations")]
    public async Task<ActionResult<IReadOnlyList<CollectionCandidateDto>>> GetPayorObligations(
        Guid payorId, CancellationToken ct) =>
        HandleResponse(await composer.GetPayorObligationsAsync(payorId, ct));

    [HttpGet("payors")]
    public async Task<ActionResult<IReadOnlyList<CollectionPayorDto>>> SearchPayors(
        [FromQuery] string? search, CancellationToken ct) =>
        HandleResponse(await composer.SearchCollectionPayorsAsync(search, ct));

    [HttpGet("official-receipts/available")]
    public async Task<ActionResult<IReadOnlyList<EcfAvailableDocumentDto>>> GetAvailableReceipts(CancellationToken ct) =>
        HandleResponse(await composer.GetAvailableReceiptsAsync(ct));

    [HttpGet("activity")]
    public async Task<ActionResult<IReadOnlyList<EcfCollectionActivityDto>>> GetActivity(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        HandleResponse(await composer.GetActivityAsync(from, to, ct));

    [HttpPost("drafts/ecf-lines")]
    public async Task<ActionResult<EcfCollectionDraftDto>> AddEcfLine(
        [FromBody] AddEcfDraftLineRequest request, CancellationToken ct) =>
        HandleResponse(await composer.AddEcfLineAsync(request, ct));

    /// <summary>Adds one approved penalty to the current OR draft; the server resolves the definition and validates the amount.</summary>
    [HttpPost("drafts/penalty-lines")]
    public async Task<ActionResult<EcfCollectionDraftDto>> AddPenaltyLine(
        [FromBody] AddPenaltyDraftLineRequest request, CancellationToken ct) =>
        HandleResponse(await composer.AddPenaltyLineAsync(request, ct));

    [HttpPost("drafts/obligation-allocations")]
    public async Task<ActionResult<EcfCollectionDraftDto>> AddObligationAllocation(
        [FromBody] AddObligationDraftAllocationRequest request, CancellationToken ct) =>
        HandleResponse(await composer.AddObligationAllocationAsync(request, ct));

    [HttpPost("drafts/rent-allocations")]
    public async Task<ActionResult<EcfCollectionDraftDto>> AddRentAllocation(
        [FromBody] AddRentDraftAllocationRequest request, CancellationToken ct) =>
        HandleResponse(await composer.AddRentAllocationAsync(request, ct));

    [HttpPut("drafts/{draftId:guid}/allocations")]
    public async Task<ActionResult<EcfCollectionDraftDto>> UpdateAllocation(
        Guid draftId, [FromBody] UpdateCollectionDraftAllocationRequest request, CancellationToken ct) =>
        HandleResponse(await composer.UpdateDraftAllocationAsync(draftId, request, ct));

    [HttpPut("drafts/{draftId:guid}/document")]
    public async Task<ActionResult<EcfCollectionDraftDto>> SelectDocument(
        Guid draftId, [FromBody] SelectEcfDraftDocumentRequest request, CancellationToken ct) =>
        HandleResponse(await composer.SelectDocumentAsync(draftId, request, ct));

    [HttpPost("drafts/{draftId:guid}/resume")]
    public async Task<ActionResult<EcfCollectionDraftDto>> ResumeDraft(
        Guid draftId, [FromBody] EcfDraftRevisionRequest request, CancellationToken ct) =>
        HandleResponse(await composer.ResumeDraftAsync(draftId, request, ct));

    [HttpPost("drafts/{draftId:guid}/review")]
    public async Task<ActionResult<EcfCollectionDraftDto>> Review(
        Guid draftId, [FromBody] EcfDraftRevisionRequest request, CancellationToken ct) =>
        HandleResponse(await composer.ReviewAsync(draftId, request, ct));

    [HttpPost("drafts/{draftId:guid}/discard")]
    public async Task<ActionResult<EcfCollectionDraftDto>> Discard(
        Guid draftId, [FromBody] EcfDraftRevisionRequest request, CancellationToken ct) =>
        HandleResponse(await composer.DiscardAsync(draftId, request, ct));

    [HttpPost("drafts/{draftId:guid}/post")]
    public async Task<ActionResult<EcfPostOutcomeDto>> Post(
        Guid draftId, [FromBody] PostEcfCollectionDraftRequest request, CancellationToken ct) =>
        HandleResponse(await composer.PostAsync(draftId, request, ct));
}
