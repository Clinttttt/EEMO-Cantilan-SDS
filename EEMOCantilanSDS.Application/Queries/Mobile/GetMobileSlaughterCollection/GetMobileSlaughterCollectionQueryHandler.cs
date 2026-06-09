using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileSlaughterCollection;

public sealed class GetMobileSlaughterCollectionQueryHandler(
    ICollectorRepository collectorRepository,
    ISlaughterRepository slaughterRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetMobileSlaughterCollectionQuery, Result<MobileSlaughterCollectionDto>>
{
    public async Task<Result<MobileSlaughterCollectionDto>> Handle(GetMobileSlaughterCollectionQuery request, CancellationToken ct)
    {
        if (currentUser.CollectorId is not { } collectorId)
            return Result<MobileSlaughterCollectionDto>.Forbidden();

        var collector = await collectorRepository.GetByIdAsync(collectorId, ct);
        if (collector is null)
            return Result<MobileSlaughterCollectionDto>.NotFound();

        if (!collector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.SLH))
            return Result<MobileSlaughterCollectionDto>.Forbidden();

        var date = new DateOnly(request.Year, request.Month, request.Day);
        var collection = await slaughterRepository.GetMobileSlaughterCollectionAsync(date, ct);

        return Result<MobileSlaughterCollectionDto>.Success(collection);
    }
}
