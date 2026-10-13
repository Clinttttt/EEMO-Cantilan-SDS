using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// The Head's view of Collections recorded directly by Collectors, with an append-only administrative review. Head/Admin only. Reading is
/// over the canonical Collections; a review adds an audit event and never creates, changes or removes a Collection, an SRC or any amount.
/// </summary>
[ApiController]
[Route("api/direct-collection-review")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class DirectCollectionReviewController(ISender sender, DirectCollectionReviewWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<DirectCollectionReviewPageDto>> Get(
        [FromQuery] string? state, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? operation, [FromQuery] string? collector,
        [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 25, CancellationToken ct = default) =>
        HandleResponse(await workflow.GetAsync(new(state ?? "Pending", from, to, operation, collector, search, skip, take), ct));

    [HttpGet("{collectionId:guid}")]
    public async Task<ActionResult<DirectCollectionDetailDto>> Detail(Guid collectionId, CancellationToken ct = default) =>
        HandleResponse(await workflow.GetDetailAsync(collectionId, ct));

    [HttpPost("review")]
    public async Task<ActionResult<DirectCollectionReviewRowDto>> Review([FromBody] DirectCollectionReviewActionRequest request, CancellationToken ct = default) =>
        HandleResponse(await workflow.ReviewAsync(request, ct));

    [HttpPost("confirm-many")]
    public async Task<ActionResult<DirectCollectionBulkConfirmResult>> ConfirmMany([FromBody] DirectCollectionBulkConfirmRequest request, CancellationToken ct = default) =>
        HandleResponse(await workflow.BulkConfirmAsync(request, ct));
}
