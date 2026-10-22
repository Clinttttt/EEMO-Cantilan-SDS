using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmCollection;

public sealed class GetMobileNpmCollectionQueryHandler(
    ICollectorRepository collectorRepository,
    IStallMobileQueries mobileQueries,
    ICurrentUserService currentUser, IClock clock,
    EEMOCantilanSDS.Application.Common.Interface.Persistence.IFacilityRepository? facilities = null) : IRequestHandler<GetMobileNpmCollectionQuery, Result<MobileNpmCollectionDto>>
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

        // A Public Market its LGU rents by the month has no daily list or daily arrears: the device collects its rent on the monthly screen.
        if (facilities is not null && await facilities.GetByCodeAsync(FacilityCode.NPM, ct) is { IsMonthlyRentMarket: true })
            return Result<MobileNpmCollectionDto>.Forbidden();

        var collection = await mobileQueries.GetMobileNpmCollectionAsync(
            request.Year,
            request.Month,
            clock.PhilippineToday,
            ct);

        return Result<MobileNpmCollectionDto>.Success(collection);
    }
}