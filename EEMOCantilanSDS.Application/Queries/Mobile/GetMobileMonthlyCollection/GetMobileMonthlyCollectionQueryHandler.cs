using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;

public sealed class GetMobileMonthlyCollectionQueryHandler(
    ICollectorRepository collectorRepository,
    IStallRepository stallRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetMobileMonthlyCollectionQuery, Result<MobileMonthlyCollectionDto>>
{
    public async Task<Result<MobileMonthlyCollectionDto>> Handle(GetMobileMonthlyCollectionQuery request, CancellationToken ct)
    {
        if (!MonthlyRentalFacilities.Codes.Contains(request.Facility))
            return Result<MobileMonthlyCollectionDto>.Forbidden();

        if (currentUser.CollectorId is not { } collectorId)
            return Result<MobileMonthlyCollectionDto>.Forbidden();

        var collector = await collectorRepository.GetByIdAsync(collectorId, ct);
        if (collector is null)
            return Result<MobileMonthlyCollectionDto>.NotFound();

        var isAssigned = collector.FacilityAssignments.Any(a => a.FacilityCode == request.Facility);
        if (!isAssigned)
            return Result<MobileMonthlyCollectionDto>.Forbidden();

        var collection = await stallRepository.GetMobileMonthlyCollectionAsync(
            request.Facility,
            request.Year,
            request.Month,
            PhilippineTime.Today,
            ct);

        return Result<MobileMonthlyCollectionDto>.Success(collection);
    }
}
