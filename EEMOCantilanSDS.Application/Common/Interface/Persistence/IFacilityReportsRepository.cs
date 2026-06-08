using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IFacilityReportsRepository
{
    Task<FacilityReportsDto> GetFacilityReportsAsync(
        FacilityCode facilityCode,
        ReportPeriod period,
        int year,
        int? month,
        int? weekNumber,
        CancellationToken ct
    );

    Task<FacilityHistoryDto> GetFacilityHistoryAsync(
        FacilityCode facilityCode,
        int year,
        CancellationToken ct
    );

    /// <summary>
    /// Lean month snapshot (collected, pending, paid/partial/unpaid counts, occupied stalls,
    /// collection rate) using the same canonical aggregation as the full report — for the dashboard
    /// facility cards. Stall-based facilities only (NPM/TCC/NCC/BBQ/ICE).
    /// </summary>
    Task<FacilitySnapshotDto> GetFacilitySnapshotAsync(
        FacilityCode facilityCode,
        int year,
        int month,
        CancellationToken ct
    );
}
