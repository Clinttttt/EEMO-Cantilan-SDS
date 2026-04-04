using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IFacilityRepository
{
    Task<FacilitySummaryDto> GetSummaryAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct);
}
