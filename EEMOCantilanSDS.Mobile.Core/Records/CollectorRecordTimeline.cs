using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Mobile.Records;

/// <summary>
/// Combines financially distinct legacy records and posted canonical collections into one
/// display timeline, ordered by actual Philippine recording time, never by facility or charge.
/// The legacy payor/day consolidation and canonical SRC-per-collection grouping remain intact.
/// </summary>
public sealed record CollectorRecordTimelineItem(
    DateTime RecordedAt, CollectorRecordEntry? Legacy = null, OperationRecord? Canonical = null);

public static class CollectorRecordTimeline
{
    public static IReadOnlyList<CollectorRecordTimelineItem> Build(
        IEnumerable<MobileCollectorRecordDto> legacy,
        IEnumerable<OperationRecord> canonical)
    {
        var legacyRows = legacy
            .GroupBy(r => r.CollectedAt.Date)
            .SelectMany(g => CollectorRecordGrouping.Build(g.OrderByDescending(x => x.CollectedAt)))
            .Select(r => new CollectorRecordTimelineItem(r.LatestAt, Legacy: r));

        var canonicalRows = canonical.Select(r =>
            new CollectorRecordTimelineItem(
                r.RecordedAtUtc is { } recorded
                    ? PhilippineTime.ToPhilippineTime(recorded)
                    : r.BusinessDate.ToDateTime(TimeOnly.MinValue),
                Canonical: r));

        return legacyRows.Concat(canonicalRows)
            .OrderByDescending(x => x.RecordedAt)
            .ThenByDescending(x => x.Canonical?.ReferenceCode ?? x.Legacy?.Primary.ORNumber ?? "", StringComparer.Ordinal)
            .ToList();
    }
}
