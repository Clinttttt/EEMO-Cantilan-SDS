using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace EEMOCantilanSDS.Api.Hubs;

/// <summary>
/// Realtime channel for staff: the server pushes "online payment received" alerts; clients don't invoke
/// anything. Restricted to admins/heads (the audience that reconciles online payments).
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin,Collector")]
public class OnlinePaymentHub : Hub
{
    /// <summary>Event name clients subscribe to.</summary>
    public const string PaymentReceivedEvent = "OnlinePaymentReceived";
}
