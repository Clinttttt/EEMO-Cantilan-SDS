using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IFacilityRepository
{
    Task<Facility?> GetByCodeAsync(FacilityCode facilityCode, CancellationToken ct);
    Task<IReadOnlyDictionary<FacilityCode, string>> GetFacilityNamesAsync(CancellationToken ct);
    Task<FacilitySummaryDto> GetSummaryAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct);
    Task<IReadOnlyList<FacilitySidebarSummaryDto>> GetSidebarSummariesAsync(int year, int month, CancellationToken ct);
}
