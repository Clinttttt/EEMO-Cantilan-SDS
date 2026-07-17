using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Notifications.SendCollectorNotification;

public sealed class SendCollectorNotificationCommandHandler(IPushSender pushSender)
    : IRequestHandler<SendCollectorNotificationCommand, Result<int>>
{
    public async Task<Result<int>> Handle(SendCollectorNotificationCommand request, CancellationToken ct)
    {
        if (request.CollectorId == Guid.Empty)
        {
            return Result<int>.Failure("A collector id is required.", 400);
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Result<int>.Failure("A notification title is required.", 400);
        }

        var reached = await pushSender.SendToCollectorAsync(
            request.CollectorId,
            request.Title.Trim(),
            (request.Body ?? string.Empty).Trim(),
            data: null,
            ct);

        return Result<int>.Success(reached);
    }
}
