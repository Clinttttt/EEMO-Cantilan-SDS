using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityPayment;
using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityReading;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Application.Queries.Utilities.GetStallUtilityHistory;
using EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityBillForEntry;
using EEMOCantilanSDS.Application.Queries.Utilities.GetUtilityRegister;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/utilities")]
[ApiController]
[Authorize(Roles = "SuperAdmin,Admin")]
public class UtilitiesController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>End-of-month electricity &amp; water billing register for the active NPM stalls.</summary>
    [HttpGet("register")]
    public async Task<ActionResult<UtilityRegisterDto>> GetRegister(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] MarketSection? section)
        => HandleResponse(await Sender.Send(new GetUtilityRegisterQuery(year, month, section)));

    /// <summary>A stall's full utility history (all months), newest first.</summary>
    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<UtilityHistoryRowDto>>> GetHistory([FromQuery] Guid stallId)
        => HandleResponse(await Sender.Send(new GetStallUtilityHistoryQuery(stallId)));

    /// <summary>Seed for the entry modal — the existing bill (edit) or carry-forward previous readings.</summary>
    [HttpGet("bill")]
    public async Task<ActionResult<UtilityBillEntryDto>> GetBillForEntry(
        [FromQuery] Guid stallId, [FromQuery] int year, [FromQuery] int month)
        => HandleResponse(await Sender.Send(new GetUtilityBillForEntryQuery(stallId, year, month)));

    /// <summary>Record/update an NPM stall's meter readings and per-bill rates for a billing month.</summary>
    [HttpPost("reading")]
    public async Task<ActionResult<UtilityBillDto>> RecordReading([FromBody] RecordUtilityReadingCommand command)
        => HandleResponse(await Sender.Send(command));

    /// <summary>Record a collection (Paid / Partial / Unpaid) against a utility bill.</summary>
    [HttpPost("payment")]
    public async Task<ActionResult<UtilityBillDto>> RecordPayment([FromBody] RecordUtilityPaymentCommand command)
        => HandleResponse(await Sender.Send(command));
}
