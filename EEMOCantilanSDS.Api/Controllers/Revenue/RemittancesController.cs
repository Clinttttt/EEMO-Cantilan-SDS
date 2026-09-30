using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Remittance and liquidation (IA-052): money already collected and turned over, recorded against whole posted Collections
/// exactly once. Nothing here creates a Collection or revenue, and there is no Treasury approval step.
/// </summary>
[ApiController]
[Route("api/remittances")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class RemittancesController(ISender sender, RemittanceWorkflow workflow) : ApiBaseController(sender)
{
    [HttpGet("scope")]
    public async Task<ActionResult<RemittanceScopeDto>> ScopeAsync(
        [FromQuery] Guid collectorId, [FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] RevenueInstrumentType? instrument, CancellationToken ct) =>
        HandleResponse(await workflow.GetScopeAsync(collectorId, from, to, instrument, ct));

    [HttpPost]
    public async Task<ActionResult<RemittanceDetailDto>> RecordAsync(
        [FromBody] RecordRemittanceRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.RecordAsync(request, ct));

    [HttpPost("{id:guid}/void")]
    public async Task<ActionResult<RemittanceDetailDto>> VoidAsync(
        Guid id, [FromBody] VoidRemittanceRequest request, CancellationToken ct) =>
        HandleResponse(await workflow.VoidAsync(id, request, ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RemittanceRowDto>>> RegisterAsync(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? collectorId,
        [FromQuery] RevenueInstrumentType? instrument, [FromQuery] RemittanceStatus? status, CancellationToken ct) =>
        HandleResponse(await workflow.GetRegisterAsync(from, to, collectorId, instrument, status, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RemittanceDetailDto>> DetailAsync(Guid id, CancellationToken ct) =>
        HandleResponse(await workflow.GetDetailAsync(id, ct));

    [HttpGet("position")]
    public async Task<ActionResult<AccountabilityPositionDto>> PositionAsync(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        HandleResponse(await workflow.GetPositionAsync(from, to, ct));
}
