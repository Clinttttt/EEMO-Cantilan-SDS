using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Notifications.RegisterDeviceToken;

/// <summary>Registers the calling collector's device FCM token so the server can push to it.</summary>
public record RegisterDeviceTokenCommand(string Token, string Platform) : IRequest<Result<bool>>;
