using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Office Cash Turnover (default off until activated). Reads: Head and Admin. Record and void: the Head only, enforced here and again in
/// the workflow. Collectors have no access. Independent of collector remittance.
/// </summary>
[Route("api/collections/office-turnovers")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class OfficeCashTurnoversController(ISender sender, OfficeCashTurnoverWorkflow turnovers) : ApiBaseController(sender)
{
    [HttpGet("availability")]
    public async Task<ActionResult<bool>> Availability() => HandleResponse(await turnovers.IsAvailableAsync());

    [HttpGet("workspace")]
    public async Task<ActionResult<OfficeCashWorkspaceDto>> Workspace([FromQuery] OfficeCashFilterRequest request, CancellationToken ct) =>
        HandleResponse(await turnovers.GetWorkspaceAsync(request, ct));

    [HttpGet("position")]
    public async Task<ActionResult<OfficeCashPositionDto>> Position([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        HandleResponse(await turnovers.GetPositionAsync(from, to, ct));

    [HttpGet("review")]
    public async Task<ActionResult<OfficeCashTurnoverReviewDto>> Review([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        HandleResponse(await turnovers.GetReviewAsync(new(from, to), ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OfficeCashTurnoverRowDto>>> Register([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        HandleResponse(await turnovers.GetRegisterAsync(from, to, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OfficeCashTurnoverDetailDto>> Detail(Guid id, CancellationToken ct) =>
        HandleResponse(await turnovers.GetDetailAsync(id, ct));

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<OfficeCashTurnoverDetailDto>> Record([FromBody] RecordOfficeCashTurnoverRequest request, CancellationToken ct) =>
        HandleResponse(await turnovers.RecordAsync(request, ct));

    [HttpPost("{id:guid}/void")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<OfficeCashTurnoverDetailDto>> Void(Guid id, [FromBody] VoidOfficeCashTurnoverRequest request, CancellationToken ct) =>
        HandleResponse(await turnovers.VoidAsync(id, request, ct));
}
