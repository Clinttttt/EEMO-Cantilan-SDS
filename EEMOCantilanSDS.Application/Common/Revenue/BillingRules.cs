using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Whether a month of rent may be taken now, under the office's billing rules (IA-071). Every writer of a monthly payment asks here so the office, the
/// collector and online checkout cannot disagree. "Open" is what the office's own statement says is unpaid, never a second reckoning. Money collected
/// and queued while offline has already changed hands, so its callers skip this check.
/// </summary>
public sealed class BillingRules(BillingPolicyWorkflow policy, IFacilityReportsRepository reports, IAppDbContext db, IClock clock)
{
    private const int AnnualMonths = 12;

    /// <summary>Why this month cannot be taken now, or null when it can. Months in <paramref name="coveredByThisReceipt"/> are being paid together with it.</summary>
    public async Task<string?> RefusalAsync(Guid stallId, int year, int month, CancellationToken ct, IReadOnlyCollection<(int Year, int Month)>? coveredByThisReceipt = null)
    {
        var rules = await policy.CurrentAsync(ct);
        var today = clock.PhilippineToday;
        var ahead = (year - today.Year) * 12 + month - today.Month;
        if (ahead > rules.MaxAdvanceMonths)
            return rules.MaxAdvanceMonths == 0
                ? "This office does not take rent for a month that has not begun."
                : $"This office takes rent at most {rules.MaxAdvanceMonths} month{(rules.MaxAdvanceMonths == 1 ? "" : "s")} ahead.";
        return rules.OldestMonthFirst ? await OlderMonthRefusalAsync(rules, stallId, year, month, ct, coveredByThisReceipt) : null;
    }

    private async Task<string?> OlderMonthRefusalAsync(BillingPolicy rules, Guid stallId, int year, int month, CancellationToken ct, IReadOnlyCollection<(int Year, int Month)>? coveredByThisReceipt = null)
    {
        var today = clock.PhilippineToday;
        var statement = await reports.GetStallStatementAsync(stallId, ct);
        if (statement is null) return null;

        var requested = new DateOnly(year, month, 1);
        var open = statement.UnpaidMonths.Select(m => (m.Year, m.Month)).ToList();
        if (statement.CurrentBalance > 0m) open.Add((today.Year, today.Month));
        var older = open.Where(m => rules.PaidInOrder(m.Year, m.Month, today) && new DateOnly(m.Year, m.Month, 1) < requested && coveredByThisReceipt?.Contains(m) != true).ToList();
        if (older.Count == 0) return null;

        // A month that cannot be paid right now must not hold up the ones after it.
        var paused = await PausedMonthsAsync(stallId, ct);
        var first = older.Where(m => !paused.Contains(m)).OrderBy(m => m.Year).ThenBy(m => m.Month).Cast<(int Year, int Month)?>().FirstOrDefault();
        return first is { } oldest
            ? $"Pay {new DateOnly(oldest.Year, oldest.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture)} first. This office takes the oldest unpaid month first."
            : null;
    }

    /// <summary>
    /// What the office can take ahead on this space right now: the unbroken run of unpaid billed months starting at the first one open from this month on,
    /// and how many of them one receipt may cover. Asked both to offer the months and to record them, so the two cannot disagree. A year is its own
    /// permission, because the limit on months ahead would otherwise rule it out.
    /// </summary>
    public async Task<PayAheadOptionsDto> PayAheadAsync(Stall stall, CancellationToken ct)
    {
        if (stall.Status != StallStatus.Active || stall.Facility?.Code == FacilityCode.NPM && !stall.Facility.IsMonthlyRentMarket)
            return new([], [], null);

        var rules = await policy.CurrentAsync(ct);
        var today = clock.PhilippineToday;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        if (rules.OldestMonthFirst && await OlderMonthRefusalAsync(rules, stall.Id, today.Year, today.Month, ct) is { } refusal)
            return new([], [], refusal);

        var rows = await db.PaymentRecords.AsNoTracking()
            .Where(r => r.StallId == stall.Id && (r.BillingYear > today.Year || r.BillingYear == today.Year && r.BillingMonth >= today.Month))
            .ToListAsync(ct);
        var excused = await db.StallMonthlyExceptions.AsNoTracking()
            .Where(e => e.StallId == stall.Id && (e.BillingYear > today.Year || e.BillingYear == today.Year && e.BillingMonth >= today.Month))
            .Select(e => new { e.BillingYear, e.BillingMonth })
            .ToListAsync(ct);
        var paused = await PausedMonthsAsync(stall.Id, ct);

        var run = new List<PayAheadMonthDto>();
        for (var i = 0; i < AnnualMonths * 2 && run.Count < AnnualMonths; i++)
        {
            var month = thisMonth.AddMonths(i);
            var (y, m) = (month.Year, month.Month);
            var row = rows.FirstOrDefault(r => r.BillingYear == y && r.BillingMonth == m);
            if (paused.Contains((y, m)) || row is { SettlementAuthorityState: not SettlementAuthority.Legacy }) break;

            var settled = row?.BalanceDue <= 0m || excused.Any(e => e.BillingYear == y && e.BillingMonth == m)
                || stall.OccupancyAnsweringForMonth(y, m, today)?.Contract.BillsCalendarMonth(y, m) != true;
            if (settled)
            {
                if (run.Count > 0) break;
                continue;
            }

            var amount = row?.BalanceDue ?? stall.MonthlyRentFor(y, m, today);
            if (amount <= 0m) break;
            run.Add(new(y, m, amount));
        }

        var withinLimit = run.TakeWhile(r => (r.Year - today.Year) * 12 + r.Month - today.Month <= rules.MaxAdvanceMonths).Count();
        var counts = Enumerable.Range(1, withinLimit).ToList();
        if (rules.AnnualPaymentAllowed && run.Count == AnnualMonths && withinLimit < AnnualMonths) counts.Add(AnnualMonths);
        return new(counts.Count == 0 ? [] : run.Take(counts[^1]).ToList(), counts, null);
    }

    /// <summary>Months held for the office: a source being moved to canonical settlement, or a captured payment under review.</summary>
    internal async Task<HashSet<(int Year, int Month)>> PausedMonthsAsync(Guid stallId, CancellationToken ct)
    {
        var months = await db.PaymentRecords.AsNoTracking()
            .Where(r => r.StallId == stallId && (r.SettlementAuthorityState == SettlementAuthority.PendingCutover
                || db.OnlinePaymentTransactions.Any(t => t.PaymentRecordId == r.Id && t.Status == OnlinePaymentStatus.ReconciliationRequired && t.Disposition == null)))
            .Select(r => new { r.BillingYear, r.BillingMonth })
            .ToListAsync(ct);
        return months.Select(m => (m.BillingYear, m.BillingMonth)).ToHashSet();
    }
}
