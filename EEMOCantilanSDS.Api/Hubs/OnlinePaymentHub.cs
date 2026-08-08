using System.Security.Claims;
using EEMOCantilanSDS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EEMOCantilanSDS.Api.Hubs;

/// <summary>
/// Realtime channel for staff: the server pushes "online payment received" alerts; clients don't invoke
/// anything. Restricted to admins/heads (the audience that reconciles online payments).
///
/// <para>
/// Every connection joins a group for its own LGU, and alerts are addressed to that group. Alerts used to go to
/// <c>Clients.All</c>, so an online payment settled for one municipality raised a toast — payor reference, billing
/// period and peso amount — on the screen of every collector and administrator of every other municipality on the
/// platform. A connection whose token carries no municipality joins no group and therefore receives nothing, which is
/// the safe direction to fail.
/// </para>
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin,Collector")]
public class OnlinePaymentHub : Hub
{
    /// <summary>Event name clients subscribe to.</summary>
    public const string PaymentReceivedEvent = "OnlinePaymentReceived";

    /// <summary>The group carrying one LGU's alerts. Must match what the notifier addresses.</summary>
    public static string GroupFor(string tenantCode) => $"tenant:{tenantCode.Trim().ToLowerInvariant()}";

    public override async Task OnConnectedAsync()
    {
        var tenantCode = Context.User?.FindFirst(AppClaimTypes.Municipality)?.Value;
        if (!string.IsNullOrWhiteSpace(tenantCode))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(tenantCode));

        await base.OnConnectedAsync();
    }
}
