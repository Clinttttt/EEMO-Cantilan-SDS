using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Application.Queries.DailyCollections.GetDailyCollectionMonth;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize]
public class DailyCollectionsController(ISender sender) : ApiBaseController(sender)
{
    [HttpPost("record")]
    public async Task<ActionResult<bool>> RecordDailyCollection([FromBody] RecordDailyCollectionCommand command)
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
