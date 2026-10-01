using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Mobile.Records;

/// <summary>One physical document (or walk-up collection) with the revenue lines it covers.</summary>
public sealed record OperationRecord(
    Guid CollectionId,
    DateOnly BusinessDate,
    string? DocumentNumber,
    RevenueInstrumentType? Instrument,
    string? PayorName,
    string Status,
    decimal Total,
    IReadOnlyList<OperationRecordLine> Lines);

public sealed record OperationRecordLine(string ClassificationName, string SourceLabel, decimal Amount);

/// <summary>
/// Turns the server's per-line register rows into one record per collection, so an itemized Official Receipt reads as ONE
/// document with its lines and never as several physical receipts. Amounts are the server's; nothing is recomputed from
/// the local queue.
/// </summary>
public static class OperationRecordGrouping
{
    public static IReadOnlyList<OperationRecord> Group(IEnumerable<CollectionRegisterRowDto> rows) =>
        rows.GroupBy(x => x.CollectionId)
            .Select(g =>
            {
                var first = g.First();
                var lines = g.Select(x => new OperationRecordLine(x.ClassificationName, x.SourceLabel, x.Amount + x.CorrectionEffect)).ToList();
                // A collection is Posted only when every line is; any correction is shown rather than hidden.
                var status = g.All(x => x.Status == "Posted") ? "Posted" : g.Any(x => x.Status == "Reversed") ? "Reversed" : "Corrected";
                return new OperationRecord(g.Key, first.BusinessDate, first.DocumentNumber, first.Instrument, first.PayorName, status,
                    lines.Sum(x => x.Amount), lines);
            })
            .OrderByDescending(x => x.BusinessDate).ThenBy(x => x.DocumentNumber, StringComparer.Ordinal)
            .ToList();
}
