using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;
using EEMOCantilanSDS.Application.Command.Payments.SetMonthlyException;
using EEMOCantilanSDS.Application.Command.Payments.ClearMonthlyException;
using EEMOCantilanSDS.Application.Command.Payments.SetMarketClosure;
using EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Queries.Payments.GetFacilityPaymentRecords;
using EEMOCantilanSDS.Application.Queries.Payments.GetMonthlyExceptions;
using EEMOCantilanSDS.Application.Queries.Payments.GetMarketClosures;
using EEMOCantilanSDS.Application.Queries.Payments.GetNpmDailyStatus;
using EEMOCantilanSDS.Application.Queries.Payments.GetPaymentRecord;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin,Collector")]
public class PaymentsController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("stall/{stallId}")]
    public async Task<ActionResult<PaymentRecordDto>> GetPaymentRecord(Guid stallId, [FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetPaymentRecordQuery(stallId, year, month);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpGet("facility/{facilityCode}")]
    public async Task<ActionResult<IReadOnlyList<FacilityPaymentRecordDto>>> GetFacilityPaymentRecords(FacilityCode facilityCode, [FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetFacilityPaymentRecordsQuery(facilityCode, year, month));
        return HandleResponse(result);
    }

    [HttpGet("facility/{facilityCode}/daily-status")]
    public async Task<ActionResult<IReadOnlyList<NpmStallDailyStatusDto>>> GetNpmDailyStatus(FacilityCode facilityCode, [FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetNpmDailyStatusQuery(facilityCode, year, month));
        return HandleResponse(result);
    }

    [HttpPost("record")]
    public async Task<ActionResult<bool>> RecordPayment([FromBody] RecordPaymentCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPost("or-number")]
    public async Task<ActionResult<bool>> SaveOrNumber([FromBody] SaveOrNumberCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    // ── Monthly excused exceptions (TCC/NCC/BBQ/ICE) — Admin/Head only ──
    [HttpGet("stall/{stallId}/monthly-exceptions")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<IReadOnlyList<int>>> GetMonthlyExceptions(Guid stallId, [FromQuery] int year)
    {
        var result = await Sender.Send(new GetStallMonthlyExceptionsQuery(stallId, year));
        return HandleResponse(result);
    }

    [HttpPost("monthly-exception")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> SetMonthlyException([FromBody] SetStallMonthlyExceptionCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPost("monthly-exception/clear")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> ClearMonthlyException([FromBody] ClearStallMonthlyExceptionCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpGet("market-closures")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<IReadOnlyList<int>>> GetMarketClosures([FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetNpmMarketClosuresQuery(year, month));
        return HandleResponse(result);
    }

    [HttpPost("market-closure")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> SetMarketClosure([FromBody] SetNpmMarketClosureCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpPost("market-closure/clear")]
    [Authorize(Roles = "SuperAdmin,Admin")]
    public async Task<ActionResult<bool>> ClearMarketClosure([FromBody] ClearNpmMarketClosureCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
}
