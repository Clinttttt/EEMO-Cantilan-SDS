using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EEMOCantilanSDS.Api.Hubs;

/// <summary>
/// Per-payor realtime channel. On connect, each client joins a group keyed by its OWN payor id (taken
/// from the authenticated JWT, never from the client), so the server can push an event to exactly one
/// payor and never leak another payor's data. Clients only receive; they invoke nothing.
/// </summary>
[Authorize(Roles = "Payor")]
public class PayorNotificationHub : Hub
{
    /// <summary>Event name clients subscribe to: their Official Receipt has been encoded.</summary>
    public const string OrIssuedEvent = "OnlinePaymentOrIssued";

    public static string GroupFor(string payorUserId) => $"payor:{payorUserId}";

    public override async Task OnConnectedAsync()
    {
        var payorId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(payorId))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(payorId));

        await base.OnConnectedAsync();
    }
}
