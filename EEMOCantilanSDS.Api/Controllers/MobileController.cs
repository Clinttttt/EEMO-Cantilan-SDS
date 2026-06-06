using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmCollection;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "Collector")]
[Route("api/[controller]")]
[ApiController]
public class MobileController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("menu")]
    public async Task<ActionResult<MobileMenuDto>> GetMenuAsync()
    {
        var result = await Sender.Send(new GetCollectorMobileMenuQuery());
        return HandleResponse(result);
    }

    [HttpGet("npm/collections")]
    public async Task<ActionResult<MobileNpmCollectionDto>> GetNpmCollectionsAsync([FromQuery] int year, [FromQuery] int month)
    {
        var result = await Sender.Send(new GetMobileNpmCollectionQuery(year, month));
        return HandleResponse(result);
    }

    [HttpPost("npm/collections/record")]
    public async Task<ActionResult<bool>> RecordNpmCollectionAsync([FromBody] RecordMobileNpmCollectionRequest request)
    {
        var command = new RecordDailyCollectionCommand(
            request.StallId,
            PhilippineTime.Today,
            request.IsPaid,
            request.FishKilos,
            request.ORNumber);

        var result = await Sender.Send(command);
        return HandleResponse(result);
    }
}
