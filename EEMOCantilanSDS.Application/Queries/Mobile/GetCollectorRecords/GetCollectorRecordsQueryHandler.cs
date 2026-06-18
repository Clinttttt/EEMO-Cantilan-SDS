using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorRecords;

public class GetCollectorRecordsQueryHandler(
    ICollectorRepository collectorRepository,
    IFacilityRepository facilityRepository,
    ICurrentUserService currentUser) : IRequestHandler<GetCollectorRecordsQuery, Result<IReadOnlyList<MobileCollectorRecordDto>>>
{
    public async Task<Result<IReadOnlyList<MobileCollectorRecordDto>>> Handle(GetCollectorRecordsQuery request, CancellationToken ct)
    {
        if (currentUser.CollectorId is not { } collectorId)
            return Result<IReadOnlyList<MobileCollectorRecordDto>>.Forbidden();

        var records = await collectorRepository.GetCollectorRecordsAsync(
            collectorId, request.Facility, request.FromDate, request.ToDate, ct);

        // Stamp the canonical facility display name (single source of truth) onto every row.
        var names = await facilityRepository.GetFacilityNamesAsync(ct);
        var named = records
            .Select(r => names.TryGetValue(r.FacilityCode, out var name) && !string.IsNullOrWhiteSpace(name)
                ? r with { FacilityName = name }
                : r)
            .ToList();

        return Result<IReadOnlyList<MobileCollectorRecordDto>>.Success(named);
    }
}
