using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>The office's explicit Business Payor linking: no link is ever made from a name.</summary>
[Route("api/business-payors")]
[ApiController]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class BusinessPayorsController(ISender sender, BusinessPayorWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("occupancies")]
    public async Task<ActionResult<IReadOnlyList<PayorOccupancyDto>>> Occupancies(
        [FromQuery] string? search, [FromQuery] PayorLinkFilter filter, CancellationToken ct) =>
        HandleResponse(await workflow.GetOccupanciesAsync(search, filter == 0 ? PayorLinkFilter.NeedsPayor : filter, ct));

    [HttpGet("candidates")]
    public async Task<ActionResult<IReadOnlyList<PayorCandidateDto>>> Candidates([FromQuery] string? search, CancellationToken ct) =>
        HandleResponse(await workflow.SearchPayorsAsync(search, ct));

    [HttpPost("links")]
    public async Task<ActionResult<PayorLinkOutcomeDto>> Link([FromBody] LinkPayorRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.LinkAsync(request, ct));

    [HttpPost("creations")]
    public async Task<ActionResult<PayorLinkOutcomeDto>> CreateAndLink([FromBody] CreatePayorAndLinkRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.CreateAndLinkAsync(request, ct));
}
