using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileTrmCollection;

public sealed class GetMobileTrmCollectionQueryHandler(
    ICollectorRepository collectorRepository,
    ITrmRepository trmRepository,
    ISuggestionRepository suggestionRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetMobileTrmCollectionQuery, Result<MobileTrmCollectionDto>>
{
    public async Task<Result<MobileTrmCollectionDto>> Handle(GetMobileTrmCollectionQuery request, CancellationToken ct)
    {
        if (currentUser.CollectorId is not { } collectorId)
            return Result<MobileTrmCollectionDto>.Forbidden();

        var collector = await collectorRepository.GetByIdAsync(collectorId, ct);
        if (collector is null)
            return Result<MobileTrmCollectionDto>.NotFound();

        if (!collector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.TRM))
            return Result<MobileTrmCollectionDto>.Forbidden();

        var transporters = await trmRepository.GetTransportersWithTodayTripsAsync(ct);
        var todayTrips = await trmRepository.GetTodayTripsAsync(ct);
        var (knownRoutes, knownOrgs, knownDrivers) = await trmRepository.GetKnownPickListsAsync(ct);

        // Drop any values the office has hidden (blocklisted) from the pick-lists.
        var hiddenRoutes = await suggestionRepository.GetHiddenValuesAsync(SuggestionType.TrmRoute, ct);
        var hiddenOrgs = await suggestionRepository.GetHiddenValuesAsync(SuggestionType.TrmOrganization, ct);
        var hiddenDrivers = await suggestionRepository.GetHiddenValuesAsync(SuggestionType.TrmDriver, ct);

        var routes = knownRoutes.Where(r => !hiddenRoutes.Contains(r)).ToList();
        var orgs = knownOrgs.Where(o => !hiddenOrgs.Contains(o)).ToList();
        var drivers = knownDrivers.Where(d => !hiddenDrivers.Contains(d)).ToList();

        return Result<MobileTrmCollectionDto>.Success(new MobileTrmCollectionDto(
            PhilippineTime.Today,
            FeeRates.TrmTripFee,
            todayTrips.Count,
            todayTrips.Sum(t => t.Fee),
            transporters.Count,
            transporters,
            todayTrips,
            routes,
            orgs,
            drivers));
    }
}
