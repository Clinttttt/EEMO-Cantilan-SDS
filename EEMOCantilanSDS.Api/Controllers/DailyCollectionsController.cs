using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrForDays;
using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Application.Queries.DailyCollections.GetDailyCollectionMonth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin,Collector")]
public class DailyCollectionsController(ISender sender) : ApiBaseController(sender)
{
    [HttpPost("record")]
    public async Task<ActionResult<bool>> RecordDailyCollection([FromBody] RecordDailyCollectionCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    // OR numbers are entered manually by admins/head only (never by field collectors).
    [HttpPost("or")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> SaveOrNumber([FromBody] SaveDailyCollectionOrNumberCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    // Apply one OR (receipt) to several PAID days of the same stall in one go (admin/head only) —
    // for when a single physical receipt covers multiple days of the same payor.
    [HttpPost("or-days")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> SaveOrForDays([FromBody] SaveDailyCollectionOrForDaysCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    // Settle a whole NPM month at once (admin/head only) — records the month's collectable days as paid.
    [HttpPost("settle-month")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> SettleNpmMonth([FromBody] SettleNpmMonthCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpGet("stall/{stallId}/month")]
    public async Task<ActionResult<DailyCollectionMonthDto>> GetDailyCollectionMonth(Guid stallId, [FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetDailyCollectionMonthQuery(stallId, year, month);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }
}
