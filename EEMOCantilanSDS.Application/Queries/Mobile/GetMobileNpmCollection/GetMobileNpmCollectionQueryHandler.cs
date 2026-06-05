using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmCollection;

public sealed class GetMobileNpmCollectionQueryHandler(
    ICollectorRepository collectorRepository,
    IStallRepository stallRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetMobileNpmCollectionQuery, Result<MobileNpmCollectionDto>>
{
    public async Task<Result<MobileNpmCollectionDto>> Handle(GetMobileNpmCollectionQuery request, CancellationToken ct)
    {
        if (currentUser.CollectorId is not { } collectorId)
            return Result<MobileNpmCollectionDto>.Forbidden();

        var collector = await collectorRepository.GetByIdAsync(collectorId, ct);
        if (collector is null)
            return Result<MobileNpmCollectionDto>.NotFound();

        var hasNpmAssignment = collector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM);
        if (!hasNpmAssignment)
            return Result<MobileNpmCollectionDto>.Forbidden();

        var collection = await stallRepository.GetMobileNpmCollectionAsync(
            request.Year,
            request.Month,
            PhilippineTime.Today,
            ct);

        return Result<MobileNpmCollectionDto>.Success(collection);
    }
}
