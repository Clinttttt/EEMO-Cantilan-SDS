using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
using EEMOCantilanSDS.Application.Command.Stalls.BulkImportStallholders;
using EEMOCantilanSDS.Application.Command.Stalls.RenewStallContract;
using EEMOCantilanSDS.Application.Command.Stalls.SoftDeleteStall;
using EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;
using EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;
using EEMOCantilanSDS.Application.Command.Stalls.UpdateStallDetails;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Queries.Payments.GetPaymentHistory;
using EEMOCantilanSDS.Application.Queries.Payments.GetStallCollectionHistory;
using EEMOCantilanSDS.Application.Queries.Payments.GetStallLedgerSummary;
using EEMOCantilanSDS.Application.Queries.Stalls.GetClosedStallAccounts;
using EEMOCantilanSDS.Application.Queries.Stalls.GetNpmRates;
using EEMOCantilanSDS.Application.Queries.Stalls.GetStallHoldersList;
using EEMOCantilanSDS.Application.Queries.Stalls.GetStallsByFacilityPaginated;
using EEMOCantilanSDS.Application.Requests.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
public class StallsController(ISender sender) : ApiBaseController(sender)
{
    [HttpPost]
    public async Task<ActionResult<StallDto>> CreateStall([FromBody] CreateStallCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    /// <summary>The caller LGU's currently-effective NPM daily + fish rates (for the Add Vendor UI).</summary>
    [HttpGet("npm-rates")]
    public async Task<ActionResult<NpmRatesDto>> GetNpmRates()
    {
        var result = await Sender.Send(new GetNpmRatesQuery());
        return HandleResponse(result);
    }

    [HttpPost("bulk-import")]
    public async Task<ActionResult<BulkImportResultDto>> BulkImport([FromBody] BulkImportStallholdersCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPut("{stallId}")]
    public async Task<ActionResult<StallDto>> UpdateStall(Guid stallId, [FromBody] UpdateStallCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

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

    /// <summary>
    /// Removes an INACTIVE (closed/expired) stall account — soft-delete: keeps history, frees the stall
    /// number for reuse. SuperAdmin-only; the handler rejects any stall that is still active (409).
    /// </summary>
    [HttpDelete("{stallId}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<bool>> SoftDeleteStall(Guid stallId)
    {
        var result = await Sender.Send(new SoftDeleteStallCommand(stallId));
        return HandleResponse(result);
    }

    [HttpGet("{stallId}/payment-history")]
    public async Task<ActionResult<IReadOnlyList<Application.Dtos.Payments.PaymentHistoryDto>>> GetPaymentHistory(Guid stallId)
    {
        var query = new GetPaymentHistoryQuery(stallId);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    // Unpaid months (with balance) across the whole contract — the Pay-bill form's source of truth.
    [HttpGet("{stallId}/outstanding-months")]
    public async Task<ActionResult<IReadOnlyList<Application.Dtos.Payments.PaymentHistoryDto>>> GetOutstandingMonths(Guid stallId)
    {
        var result = await Sender.Send(new Application.Queries.Payments.GetStallOutstanding.GetStallOutstandingQuery(stallId));
        return HandleResponse(result);
    }

    [HttpGet("{stallId}/ledger-summary")]
    public async Task<ActionResult<Application.Dtos.Payments.StallLedgerSummaryDto>> GetLedgerSummary(Guid stallId)
    {
        var query = new GetStallLedgerSummaryQuery(stallId);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("{stallId}/collection-history")]
    public async Task<ActionResult<CursorPagedResult<Application.Dtos.Payments.StallCollectionHistoryRowDto>>> GetCollectionHistory(
        Guid stallId, [FromQuery] DateTime? cursor = null, [FromQuery] int pageSize = 10)
    {
        var query = new GetStallCollectionHistoryQuery(stallId, cursor, pageSize);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpPatch("{stallId}/details")]
    public async Task<ActionResult<bool>> UpdateStallDetails(Guid stallId, [FromBody] UpdateStallDetailsCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpGet("closed-accounts")]
    public async Task<ActionResult<IReadOnlyList<Application.Dtos.Stalls.ClosedStallAccountDto>>> GetClosedAccounts()
    {
        var result = await Sender.Send(new GetClosedStallAccountsQuery());
        return HandleResponse(result);
    }

    [HttpPost("{stallId}/renew")]
    public async Task<ActionResult<bool>> RenewContract(Guid stallId, [FromBody] RenewStallContractRequest request)
    {
        var command = new RenewStallContractCommand(
            stallId, request.EffectivityDate, request.DurationYears, request.ActualOccupant, request.NameOnContract);
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
}

