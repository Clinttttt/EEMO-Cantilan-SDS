using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;

public sealed class GetMobileMonthlyCollectionQueryHandler(
    ICollectorRepository collectorRepository,
    IStallMobileQueries mobileQueries,
    ICurrentUserService currentUser, IClock clock,
    IFacilityRepository? facilities = null) : IRequestHandler<GetMobileMonthlyCollectionQuery, Result<MobileMonthlyCollectionDto>>
{
    public async Task<Result<MobileMonthlyCollectionDto>> Handle(GetMobileMonthlyCollectionQuery request, CancellationToken ct)
    {
        // A Public Market is monthly only where its LGU declared standard monthly rental; its stored archetype says so, nothing else does.
        var monthly = MonthlyRentalFacilities.Codes.Contains(request.Facility)
            || (request.Facility == FacilityCode.NPM && facilities is not null
                && await facilities.GetByCodeAsync(request.Facility, ct) is { IsMonthlyRentMarket: true });
        if (!monthly)
            return Result<MobileMonthlyCollectionDto>.Forbidden();

        if (currentUser.CollectorId is not { } collectorId)
            return Result<MobileMonthlyCollectionDto>.Forbidden();

        var collector = await collectorRepository.GetByIdAsync(collectorId, ct);
        if (collector is null)
            return Result<MobileMonthlyCollectionDto>.NotFound();

        var isAssigned = collector.FacilityAssignments.Any(a => a.FacilityCode == request.Facility);
        if (!isAssigned)
            return Result<MobileMonthlyCollectionDto>.Forbidden();

        var collection = await mobileQueries.GetMobileMonthlyCollectionAsync(
            request.Facility,
            request.Year,
            request.Month,
            clock.PhilippineToday,
            ct);

        return Result<MobileMonthlyCollectionDto>.Success(collection);
    }
}