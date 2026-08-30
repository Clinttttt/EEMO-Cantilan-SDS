using EEMOCantilanSDS.Application.Command.Notifications.SendCollectorNotification;
using EEMOCantilanSDS.Application.Requests.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin,Admin")]
[Route("api/[controller]")]
[ApiController]
public class NotificationsController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Send a push notification to a collector's registered devices. Returns the number reached.</summary>
    [HttpPost("collectors/{collectorId:guid}/send")]
    public async Task<ActionResult<int>> SendToCollectorAsync(Guid collectorId, [FromBody] SendCollectorNotificationRequest request)
    {
        var result = await Sender.Send(new SendCollectorNotificationCommand(collectorId, request.Title, request.Body));
        return HandleResponse(result);
    }
}
