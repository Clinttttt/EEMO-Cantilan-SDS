using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Notifications.SendCollectorNotification;

/// <summary>Sends a push notification to a collector's registered devices. Returns the number reached.</summary>
public record SendCollectorNotificationCommand(Guid CollectorId, string Title, string Body) : IRequest<Result<int>>;
