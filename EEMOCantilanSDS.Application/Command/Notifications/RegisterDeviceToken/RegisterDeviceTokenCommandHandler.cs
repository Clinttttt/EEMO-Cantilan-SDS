using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Notifications.RegisterDeviceToken;

public sealed class RegisterDeviceTokenCommandHandler(
    ICollectorDeviceTokenRepository tokenRepository,
    ICurrentUserService currentUser) : IRequestHandler<RegisterDeviceTokenCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RegisterDeviceTokenCommand request, CancellationToken ct)
    {
        // The token is attributed to the authenticated collector — never taken from the request body.
        if (currentUser.CollectorId is not { } collectorId)
        {
            return Result<bool>.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Result<bool>.Failure("A device token is required.", 400);
        }

        await tokenRepository.UpsertAsync(
            collectorId,
            request.Token,
            request.Platform,
            currentUser.MunicipalityId ?? Guid.Empty,
            ct);

        return Result<bool>.Success(true);
    }
}
