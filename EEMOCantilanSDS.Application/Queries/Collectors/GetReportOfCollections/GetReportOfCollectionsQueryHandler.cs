using EEMOCantilanSDS.Application.Common;
using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetReportOfCollections;

/// <summary>
/// Shapes the document from the receipt-level record.
///
/// <para>
/// Every figure on the sheet is derived here from the same set of lines, so the summary, the facility breakdown, the daily
/// record and the receipt listing cannot disagree with one another. What the office itself recorded is stated apart, being
/// somebody else's accountability.
/// </para>
/// </summary>
public class GetReportOfCollectionsQueryHandler(
    ICollectorRepository collectors,
    ICollectorReportQueries report)
    : IRequestHandler<GetReportOfCollectionsQuery, Result<ReportOfCollectionsDto>>
{
    public async Task<Result<ReportOfCollectionsDto>> Handle(GetReportOfCollectionsQuery request, CancellationToken ct)
    {
        if (request.To < request.From)
            return Result<ReportOfCollectionsDto>.Failure("The period ends before it begins.", ResultStatus.Invalid);

        var collector = await collectors.GetByIdAsync(request.CollectorId, ct);
        if (collector is null)
            return Result<ReportOfCollectionsDto>.NotFound();

        var data = await report.GetCollectionsAsync(request.CollectorId, request.From, request.To, ct);

        var lines = data.Lines;
        var total = lines.Sum(l => l.Amount);

        // A receipt is the unit the office answers for. Lines without a number are counted individually, since each is
        // still a collection, but they cannot be presented as a receipt.
        var receiptsIssued = lines
            .Where(l => !string.IsNullOrWhiteSpace(l.OrNumber))
            .Select(l => l.OrNumber!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var facilities = lines
            .GroupBy(l => l.Facility)
            .OrderBy(g => g.Key)
            .Select(g => new ReportFacilityLineDto(
                g.Key,
                g.Where(l => !string.IsNullOrWhiteSpace(l.OrNumber)).Select(l => l.OrNumber!).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                PayorCount(g),
                g.Sum(l => l.Amount)))
            .ToList();

        var days = lines
            .GroupBy(l => DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(l.TakenAtUtc)))
            .OrderBy(g => g.Key)
            .Select(g => new ReportDayLineDto(
                g.Key,
                ReceiptSpan(g.Where(l => !string.IsNullOrWhiteSpace(l.OrNumber)).Select(l => l.OrNumber!)),
                PayorCount(g),
                // Money that answered for a period before the one it was taken in: an owed market day, or a rental paid
                // after its month. Without it a day appears to collect more than it could possibly owe.
                g.Where(l => AnswersForAnEarlierPeriod(l, g.Key)).Sum(l => l.Amount),
                g.Sum(l => l.Amount)))
            .ToList();

        // One row per receipt, the days it covered folded into its "for" so the listing reads as the office's copy does.
        var receipts = lines
            .GroupBy(l => string.IsNullOrWhiteSpace(l.OrNumber)
                ? $"none:{l.Facility}:{l.StallNo}:{l.PayorName}:{l.TakenAtUtc:O}"
                : $"or:{l.Facility}:{l.PayorName}:{l.OrNumber!.ToUpperInvariant()}")
            .Select(g =>
            {
                var first = g.OrderBy(l => l.TakenAtUtc).First();
                return new ReportReceiptLineDto(
                    string.IsNullOrWhiteSpace(first.OrNumber) ? "—" : first.OrNumber!,
                    PhilippineTime.ToPhilippineTime(g.Max(l => l.TakenAtUtc)),
                    first.PayorName,
                    first.StallNo,
                    first.Facility,
                    FeeFor(g),
                    g.Sum(l => l.Amount));
            })
            .OrderBy(r => r.TakenAt)
            .ToList();

        return Result<ReportOfCollectionsDto>.Success(new ReportOfCollectionsDto(
            collector.Id,
            collector.FullName,
            collector.EmployeeId ?? string.Empty,
            collector.FacilityAssignments.Select(a => a.FacilityCode).OrderBy(c => c).ToList(),
            request.From,
            request.To,
            total,
            receiptsIssued,
            PayorCount(lines),
            days.Count,
            data.OfficeRecorded,
            data.OfficeReceipts,
            facilities,
            days,
            receipts,
            data.Absences
                .Select(a => new ReportAbsenceLineDto(a.Day, a.PayorName, a.StallNo, a.Facility))
                .ToList(),
            data.UtilityBilled,
            data.UtilityCollected));
    }

    /// <summary>A payor is a person at a space: one holder of two stalls owes two lines, as the office reads them.</summary>
    private static int PayorCount(IEnumerable<CollectorCollectionLine> lines) => lines
        .Select(l => $"{l.Facility}|{l.StallNo}|{l.PayorName}")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    /// <summary>
    /// Whether this money answered for a period earlier than the one it was taken in.
    ///
    /// <para>
    /// The two kinds of charge are measured differently, and conflating them would misstate both. A market day is late when
    /// its own day precedes the day the money came in. A monthly rental is late when its BILLED MONTH precedes the month the
    /// money came in: August's rent paid on the twenty fourth of August is not arrears, while July's rent paid on the same
    /// day is.
    /// </para>
    /// </summary>
    private static bool AnswersForAnEarlierPeriod(CollectorCollectionLine line, DateOnly takenOn)
        => line.BilledMonth is { } month
            ? month.Year < takenOn.Year || (month.Year == takenOn.Year && month.Month < takenOn.Month)
            : line.FeeDay is { } day && day < takenOn;

    /// <summary>
    /// The day or month a receipt answers for. Where several owed days were settled together it names the span and says how
    /// many, since that is the whole reason a receipt can exceed one day's fee.
    /// </summary>
    private static string FeeFor(IEnumerable<CollectorCollectionLine> receipt)
    {
        var lines = receipt.ToList();

        // A rental names its month, which is what tells a payment made within its month from one made after it.
        var months = lines
            .Where(l => l.BilledMonth is not null)
            .Select(l => l.BilledMonth!.Value)
            .Distinct()
            .OrderBy(m => m)
            .ToList();
        if (months.Count > 0)
            return string.Join(", ", months.Select(m => m.ToString("MMM yyyy", CultureInfo.InvariantCulture)));

        var days = lines.Where(l => l.FeeDay is not null).Select(l => l.FeeDay!.Value).Distinct().OrderBy(d => d).ToList();
        if (days.Count == 0)
            return lines.First().Nature;
        if (days.Count == 1)
            return days[0].ToString("MMM d, yyyy");

        return $"{days[0]:MMM d} to {days[^1]:MMM d, yyyy} ({days.Count} days)";
    }

    /// <summary>
    /// Receipt numbers as a range only where they are numeric and run unbroken; otherwise their count. The office types
    /// them one by one, so a stated range has to be one the numbers actually support.
    /// </summary>
    private static string ReceiptSpan(IEnumerable<string> orNumbers)
    {
        var numbers = orNumbers.Select(o => o.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (numbers.Count == 0) return "—";
        if (numbers.Count == 1) return numbers[0];

        if (numbers.All(n => long.TryParse(n, out _)))
        {
            var ordered = numbers.Select(long.Parse).OrderBy(n => n).ToList();
            var unbroken = ordered.Zip(ordered.Skip(1), (a, b) => b - a == 1).All(step => step);
            if (unbroken)
                return $"{ordered[0]} to {ordered[^1]}";
        }

        return $"{numbers.Count} receipts";
    }
}
