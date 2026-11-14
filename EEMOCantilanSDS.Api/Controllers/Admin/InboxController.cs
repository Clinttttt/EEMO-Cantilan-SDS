using EEMOCantilanSDS.Application.Common.Notifications;
using EEMOCantilanSDS.Application.Dtos.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>The signed-in Head or Admin's own messages.</summary>
[ApiController]
[Route("api/inbox")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class InboxController(ISender sender, NotificationInbox inbox) : ApiBaseController(sender)
{
    [HttpGet]
    public async Task<ActionResult<InboxPageDto>> ReadAsync(CancellationToken ct) =>
        HandleResponse(await inbox.ReadMineAsync(ct));

    [HttpPost("read")]
    public async Task<ActionResult<InboxPageDto>> MarkAllReadAsync(CancellationToken ct) =>
        HandleResponse(await inbox.MarkReadAsync(null, ct));

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<InboxPageDto>> MarkReadAsync(Guid id, CancellationToken ct) =>
        HandleResponse(await inbox.MarkReadAsync(id, ct));
}
