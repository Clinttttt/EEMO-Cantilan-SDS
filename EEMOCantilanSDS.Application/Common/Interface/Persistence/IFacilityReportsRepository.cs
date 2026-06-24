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

    /// <summary>
    /// Stalls behind on payments over the rolling 12-month window ending at (and excluding) the given
    /// month — the shared delinquency/arrears source for both the dashboard and the Financial Reports
    /// attention list. <paramref name="facility"/> null = all facilities. Returns cumulative balance and
    /// unpaid-month count per stall, ordered by months unpaid then balance (descending).
    /// </summary>
    Task<IReadOnlyList<DelinquentStallDto>> GetDelinquentStallsAsync(
        FacilityCode? facility,
        int year,
        int month,
        CancellationToken ct
    );
}
