using EEMOCantilanSDS.Application.Dtos;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// What the office reads ABOUT its collectors: the roster with each one's figures, and one collector's activity for a
/// period.
///
/// <para>
/// The office's question, not the collector's. A supervisor comparing the roster is asking who collected how much this
/// month; a collector's own app asks what to collect next — see <see cref="ICollectorMobileQueries"/> for that. Neither is
/// the account repository, which loads a collector to modify, finds one for LOGIN and rules on uniqueness.
/// </para>
/// </summary>
public interface ICollectorReportingQueries
{
    /// <summary>Every collector with their figures for the period — the office's roster view.</summary>
    Task<List<CollectorListDto>> GetAllCollectorsWithStatsAsync(int year, int month, CancellationToken cancellationToken = default);

    /// <summary>One collector's activity for a period, or null when there is no such collector.</summary>
    Task<CollectorActivityDto?> GetCollectorActivityAsync(Guid collectorId, int year, int month, CancellationToken cancellationToken = default);
}
