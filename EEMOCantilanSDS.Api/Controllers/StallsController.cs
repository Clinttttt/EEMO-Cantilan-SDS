using EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Queries.Payments.GetPaymentHistory;
using EEMOCantilanSDS.Application.Queries.Stalls.GetStallHoldersList;
using EEMOCantilanSDS.Application.Queries.Stalls.GetStallsByFacilityPaginated;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

public class StallsController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("facility/{facilityCode}/holders-list")]
    public async Task<ActionResult<StallHoldersListDto>> GetStallHoldersList(
        FacilityCode facilityCode,
        [FromQuery] MarketSection? section = null,
        [FromQuery] string? searchTerm = null)
    {
        var query = new GetStallHoldersListQuery(facilityCode, section, searchTerm);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("facility/{facilityCode}/paginated")]
    public async Task<ActionResult<CursorPagedResult<StallDto>>> GetStallsPaginated(
        FacilityCode facilityCode, 
        [FromQuery] MarketSection? section = null,
        [FromQuery] DateTime? cursor = null,
        [FromQuery] int pageSize = 20)
    {
        var query = new GetStallsByFacilityPaginatedQuery(facilityCode, section, cursor, pageSize);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpPatch("{stallId}/status")]
    public async Task<ActionResult<bool>> ToggleStatus(Guid stallId, [FromBody] ToggleStallStatusRequest request)
    {
        var command = new ToggleStallStatusCommand(stallId, request.Close);
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpGet("{stallId}/payment-history")]
    public async Task<ActionResult<IReadOnlyList<Application.Dtos.Payments.PaymentHistoryDto>>> GetPaymentHistory(Guid stallId)
    {
        var query = new GetPaymentHistoryQuery(stallId);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }
}

public record ToggleStallStatusRequest(bool Close);
