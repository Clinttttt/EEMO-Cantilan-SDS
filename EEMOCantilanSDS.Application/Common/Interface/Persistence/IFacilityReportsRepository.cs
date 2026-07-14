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
        Guid facilityId,
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

    /// <summary>
    /// As above, but <paramref name="includeClosed"/>=true also surfaces CLOSED stalls that still carry
    /// unpaid past-month records (used by the Financial Reports follow-up list). Default callers stay
    /// active-only.
    /// </summary>
    Task<IReadOnlyList<DelinquentStallDto>> GetDelinquentStallsAsync(
        FacilityCode? facility,
        int year,
        int month,
        bool includeClosed,
        CancellationToken ct
    );

    /// <summary>
    /// Per-stall recognized fish kilos for NPM in the given billing month — the volume behind the
    /// ₱1/kg fish fee. Mirrors the fee-type breakdown rule (a stall's whole-month paid monthly record
    /// FishKilos, otherwise its collectable paid daily-collection kilos). Key = StallId; stalls with
    /// no fish activity are omitted.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetNpmFishKilosByStallAsync(
        int year,
        int month,
        CancellationToken ct
    );

    /// <summary>
    /// Earliest calendar year that has any collection / billing / contract activity for this tenant —
    /// the floor for the Follow-up History year picker, so a back-dated (prior-year) settlement is
    /// reachable and not stranded outside the last-12-months window. Returns the current year when there
    /// is no data yet.
    /// </summary>
    Task<int> GetEarliestActivityYearAsync(CancellationToken ct);
}
