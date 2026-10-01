using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorReport;

/// <summary>
/// Completes the collector's Mobile report with their posted canonical Collections, exactly once.
/// </summary>
/// <remarks>
/// The legacy reader states the legacy-authoritative facility money: it skips a source row whose settlement authority has
/// moved to canonical (CollectionSourceAuthorityMap), so the canonical Collection for that money is the only copy left.
/// The canonical facts are the ones the collector's Position sums — net of corrections, grouped by their own business date,
/// classified by their CollectionLine — so Position, By Month, Per Payee and Summary state one total. Nothing here reads a
/// device queue: money waiting to sync is not collected until the server has posted it.
/// </remarks>
public static class CollectorReportComposer
{
    public static MobileCollectorReportDto Compose(
        MobileCollectorReportDto legacy, IReadOnlyList<CollectorCollectionFactDto> canonical)
    {
        // A collection corrected to nothing is no longer a transaction; a partial correction still is, at its net.
        var counted = canonical.Where(x => x.NetAmount != 0m).ToList();

        var breakdown = legacy.Transactions
            .GroupBy(t => t.FacilityName)
            .Select(g => new MobileReportBreakdownDto(g.Key, false, g.Sum(t => t.Amount), g.Count()))
            .Concat(counted
                .SelectMany(c => c.Lines.Select(l => (c.CollectionId, l.Name, l.Amount)))
                .GroupBy(x => x.Name)
                .Select(g => new MobileReportBreakdownDto(g.Key, true, g.Sum(x => x.Amount), g.Select(x => x.CollectionId).Distinct().Count())))
            .Where(b => b.Amount != 0m)
            .OrderByDescending(b => b.Amount)
            .ThenBy(b => b.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (counted.Count == 0)
            return legacy with { Breakdown = breakdown, NamedPayors = [] };

        var named = counted.Where(c => c.PayorId is not null)
            .GroupBy(c => c.PayorId!.Value)
            .Select(g => new MobileReportNamedPayorDto(
                g.Key, g.Select(c => c.PayorName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Registered payor",
                g.Sum(c => c.NetAmount), g.Count()))
            .OrderByDescending(p => p.Amount)
            .ThenBy(p => p.PayorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var unnamed = counted.Where(c => c.PayorId is null).ToList();
        var canonicalTotal = counted.Sum(c => c.NetAmount);

        var totals = legacy.Totals with
        {
            CollectedAmount = legacy.Totals.CollectedAmount + canonicalTotal,
            TransactionCount = legacy.Totals.TransactionCount + counted.Count,
            PayeeCount = legacy.Totals.PayeeCount + named.Count,
            CanonicalCollectedAmount = canonicalTotal,
            CanonicalTransactionCount = counted.Count,
            UnnamedCollectedAmount = unnamed.Sum(c => c.NetAmount),
            UnnamedTransactionCount = unnamed.Count
        };

        // Daily mode keys a period by its day; otherwise the report states the one selected month.
        DateOnly PeriodOf(DateOnly businessDate) =>
            legacy.DailyReportMode ? businessDate : new DateOnly(legacy.Year, legacy.Month, 1);

        var periods = legacy.Periods.ToDictionary(p => p.PeriodDate);
        foreach (var group in counted.GroupBy(c => PeriodOf(c.BusinessDate)))
        {
            var amount = group.Sum(c => c.NetAmount);
            var payors = group.Where(c => c.PayorId is not null).Select(c => c.PayorId).Distinct().Count();
            periods[group.Key] = periods.TryGetValue(group.Key, out var existing)
                ? existing with
                {
                    CollectedAmount = existing.CollectedAmount + amount,
                    TransactionCount = existing.TransactionCount + group.Count(),
                    PayeeCount = existing.PayeeCount + payors
                }
                : new MobileReportPeriodSummaryDto(group.Key, amount, group.Count(), payors, 0, 0, 0);
        }

        return legacy with
        {
            Totals = totals,
            Periods = periods.Values.OrderByDescending(p => p.PeriodDate).ToList(),
            Breakdown = breakdown,
            NamedPayors = named
        };
    }
}
