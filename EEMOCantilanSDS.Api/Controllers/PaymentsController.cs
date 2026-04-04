using EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Queries.Payments.GetPaymentRecord;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

public class PaymentsController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("stall/{stallId}")]
    public async Task<ActionResult<PaymentRecordDto>> GetPaymentRecord(Guid stallId, [FromQuery] int year, [FromQuery] int month)
    {
        var query = new GetPaymentRecordQuery(stallId, year, month);
        var result = await Sender.Send(query);
        return HandleResponse(result);
    }

    [HttpPost("or-number")]
    public async Task<ActionResult<bool>> SaveOrNumber([FromBody] SaveOrNumberCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
}
