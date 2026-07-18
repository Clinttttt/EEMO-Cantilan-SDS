using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Notifications.RemoveDeviceToken;

/// <summary>Unregisters a device's push token so the collector stops receiving notifications on it
/// (used when the collector turns notifications off).</summary>
public record RemoveDeviceTokenCommand(string Token) : IRequest<Result<bool>>;
