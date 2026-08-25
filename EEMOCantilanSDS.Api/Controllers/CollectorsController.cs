using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Command.Collectors.RecordCollectorRemittance;
using EEMOCantilanSDS.Application.Command.Collectors.ResetCollectorPassword;
using EEMOCantilanSDS.Application.Command.Collectors.ToggleCollectorStatus;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Requests.Collectors;
using EEMOCantilanSDS.Application.Queries.Collectors.GetAllCollectors;
using EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorById;
using EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorRemittances;
using EEMOCantilanSDS.Application.Queries.Collectors.GetNextEmployeeId;
using EEMOCantilanSDS.Application.Queries.Collectors.GetReportOfCollections;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin")]
[Route("api/[controller]")]
[ApiController]
public class CollectorsController : ApiBaseController
{
    public CollectorsController(ISender sender) : base(sender)
    {
    }
 
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CollectorListDto>>> GetAllCollectorsAsync()
    {
        var result = await Sender.Send(new GetAllCollectorsQuery());
        return HandleResponse(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CollectorActivityDto>> GetCollectorByIdAsync(Guid id)
    {
        var result = await Sender.Send(new GetCollectorByIdQuery(id));
        return HandleResponse(result);
    }

    [HttpPost]
    public async Task<ActionResult<CollectorDto>> CreateCollectorAsync([FromBody] CreateCollectorCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<bool>> UpdateCollectorAsync(Guid id, [FromBody] UpdateCollectorCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<bool>> ToggleStatusAsync(Guid id, [FromBody] ToggleCollectorStatusRequest request)
    {
        var result = await Sender.Send(new ToggleCollectorStatusCommand(id, request.IsActive));
        return HandleResponse(result);
    }

    [HttpPatch("{id:guid}/reset-password")]
    public async Task<ActionResult<bool>> ResetPasswordAsync(Guid id, [FromBody] ResetCollectorPasswordRequest request)
    {
        var result = await Sender.Send(new ResetCollectorPasswordCommand(id, request.NewPassword, request.ConfirmPassword));
        return HandleResponse(result);
    }

    [HttpGet("next-employee-id")]
    public async Task<ActionResult<string>> GetNextEmployeeIdAsync()
    {
        var result = await Sender.Send(new GetNextEmployeeIdQuery());
        return HandleResponse(result);
    }

    // ── Remittances ──
    //
    // Collector ACCOUNTS stay with the Head, which is why this controller is SuperAdmin. Receiving cash is a different
    // matter: the office decided the Head AND Administrators do it, being the officers accountable on the portal, so these
    // two carry their own role list. Nothing else on the controller is widened.

    [HttpGet("{id:guid}/remittances")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<CollectorRemittanceSummaryDto>> GetRemittancesAsync(
        Guid id, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var result = await Sender.Send(new GetCollectorRemittancesQuery(id, from, to));
        return HandleResponse(result);
    }

    /// <summary>The office's Report of Collections for one collector over one period.</summary>
    [HttpGet("{id:guid}/report-of-collections")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<ReportOfCollectionsDto>> GetReportOfCollectionsAsync(
        Guid id, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var result = await Sender.Send(new GetReportOfCollectionsQuery(id, from, to));
        return HandleResponse(result);
    }

    [HttpPost("{id:guid}/remittances")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<RemittanceRecordedDto>> RecordRemittanceAsync(
        Guid id, [FromBody] RecordCollectorRemittanceRequest request)
    {
        var result = await Sender.Send(new RecordCollectorRemittanceCommand(
            id, request.Amount, request.CoversFrom, request.CoversTo,
            request.ReceivedAt, request.ReferenceNo, request.Notes));
        return HandleResponse(result);
    }
}
