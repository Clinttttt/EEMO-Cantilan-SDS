using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Head follow-up for direct Fish/Meat Vendor Fee Collections recorded against a payer name with no registered vendor. Reading is Head/Admin;
/// resolving (register or link) is the Head only, matching the vendor registry. Nothing here creates or changes a Collection, SRC or amount.
/// </summary>
[ApiController]
[Route("api/fish-meat-payor-follow-up")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class FishMeatPayorFollowUpController(ISender sender, FishMeatPayorFollowUpWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<FishMeatPayorFollowUpPageDto>> List([FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 25, CancellationToken ct = default) =>
        HandleResponse(await workflow.ListAsync(search, skip, take, ct));

    [HttpGet("{collectionId:guid}")]
    public async Task<ActionResult<FishMeatPayorFollowUpDetailDto>> Detail(Guid collectionId, CancellationToken ct = default) =>
        HandleResponse(await workflow.DetailAsync(collectionId, ct));

    [HttpPost("resolve")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<FishMeatPayorResolveResult>> Resolve([FromBody] FishMeatPayorResolveRequest request, CancellationToken ct = default) =>
        HandleResponse(await workflow.ResolveAsync(request, ct));
}
