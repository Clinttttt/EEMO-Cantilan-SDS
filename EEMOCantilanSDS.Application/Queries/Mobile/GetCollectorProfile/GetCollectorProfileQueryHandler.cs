using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorProfile;

public sealed class GetCollectorProfileQueryHandler(
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetCollectorProfileQuery, Result<MobileCollectorProfileDto>>
{
    public async Task<Result<MobileCollectorProfileDto>> Handle(GetCollectorProfileQuery request, CancellationToken ct)
    {
        if (currentUser.CollectorId is not { } collectorId)
            return Result<MobileCollectorProfileDto>.Forbidden();

        var profile = await collectorRepository.GetCollectorProfileAsync(collectorId, ct);
        return profile is null
            ? Result<MobileCollectorProfileDto>.NotFound()
            : Result<MobileCollectorProfileDto>.Success(profile);
    }
}
