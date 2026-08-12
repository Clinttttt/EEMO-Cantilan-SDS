using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// What a collector's own app shows them about their work.
///
/// <para>
/// Three projections a collector reads about themselves: the collections they have recorded, their remittance report for
/// a period, and their profile. They are read from the phone, over a connection that drops, so each is a whole screen in
/// one payload rather than a lookup repeated per row.
/// </para>
///
/// <para>
/// Split out of <see cref="ICollectorRepository"/>, which is an account repository: it loads a collector to modify, finds
/// one by username or employee ID for LOGIN, and rules on uniqueness. A handler serving the app should not be able to
/// reach an authentication lookup, and a test for one of these screens should not have to stub seventeen members.
/// </para>
/// </summary>
public interface ICollectorMobileQueries
{
    /// <summary>
    /// The collector's own collection events (paid/partial) across their assigned facilities for a PH
    /// date range, optionally narrowed to one facility. Scoped by CollectorId, so it never leaks others'.
    /// </summary>
    Task<IReadOnlyList<MobileCollectorRecordDto>> GetCollectorRecordsAsync(
        Guid collectorId, FacilityCode? facility, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    /// <summary>This collector's remittance report for a date range: what they collected, by facility and day.</summary>
    Task<MobileCollectorReportDto> GetCollectorReportAsync(
        Guid collectorId, IReadOnlyCollection<FacilityCode> facilities, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// The authenticated collector's own profile: account fields plus lifetime collection stats
    /// (all-time recognized collected, distinct active days, assigned-facility count).
    /// </summary>
    Task<MobileCollectorProfileDto?> GetCollectorProfileAsync(Guid collectorId, CancellationToken cancellationToken = default);
}
