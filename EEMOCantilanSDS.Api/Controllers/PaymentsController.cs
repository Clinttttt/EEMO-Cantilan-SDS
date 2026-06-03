using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Queries.Payments.GetFacilityPaymentRecords;
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
}
