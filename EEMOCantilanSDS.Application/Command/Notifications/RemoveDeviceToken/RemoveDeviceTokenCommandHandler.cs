using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Notifications.RemoveDeviceToken;

public sealed class RemoveDeviceTokenCommandHandler(
    ICollectorDeviceTokenRepository tokenRepository,
    ICurrentUserService currentUser) : IRequestHandler<RemoveDeviceTokenCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RemoveDeviceTokenCommand request, CancellationToken ct)
    {
        // Only an authenticated collector may unregister; the token identifies the device to drop.
        if (currentUser.CollectorId is null)
        {
            return Result<bool>.Forbidden();
        }

        // Nothing to remove is a no-op success (idempotent — toggling off when never registered is fine).
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Result<bool>.Success(true);
        }

        await tokenRepository.RemoveByTokenAsync(request.Token, ct);
        return Result<bool>.Success(true);
    }
}
