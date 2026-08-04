using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of FacilityReportsRepository: stall-compliance row helpers.
public partial class FacilityReportsRepository
{
    #region Stall Compliance Helpers

    /// <summary>
    /// Per-stall compliance rows for the report page (powers both the delinquency table
    /// and the full "all stalls" table). Covers occupied stalls (active contract) only.
    /// Status/balance reflect the selected period; MissedMonths counts months in the
    /// period year (up to the period month) the stall was under contract yet paid nothing
    /// (NPM recognises daily collections; see <see cref="CountMissedMonths"/>).
    /// </summary>
    private async Task<IReadOnlyList<StallComplianceDto>> GenerateStallComplianceAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct,
        // Where the missed-month count starts. The report's All Stalls column counts this year (null → January),
        // while the arrears source counts a rolling twelve months and says so.
        DateOnly? countMissedFrom = null)
    {
        var stalls = (await _context.Stalls
            .AsNoTracking()
            // ALL contracts, not only the live one: a period in the past was held by whoever held it then, and a
            // stall since handed to a new lessee must still report that period under the lessee who was there.
            .Include(s => s.Contracts)
            .Where(s => s.FacilityId == facilityId
                && s.Contracts.Any())
            .ToListAsync(ct))
            // Only stalls whose contract was actually effective during the selected period.
            // Without this, a stall whose contract starts after the period (or expired before it)
            // appears with a ₱0 obligation and renders as a phantom "Paid" payor for a month it
            // did not yet operate — e.g. viewing May when every contract begins June 5.
            .Where(s => CountNpmCollectableDays(s, startDate, endDate) > 0)
            .ToList();

        if (stalls.Count == 0)
            return Array.Empty<StallComplianceDto>();

        var stallIds = stalls.Select(s => s.Id).ToList();

        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => stallIds.Contains(pr.StallId))
            .ToListAsync(ct);

        var includeFish = facilityCode == FacilityCode.NPM;
        var (complianceStart, complianceEnd) = (startDate, endDate);

        var periodPayments = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, complianceStart, complianceEnd))
            .GroupBy(pr => pr.StallId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1)).ToList());

        var stallsWithNpmPeriodPayments = includeFish
            ? periodPayments
                .Where(kvp => kvp.Value.Any(pr => pr.Status != PaymentStatus.Unpaid))
                .Select(kvp => kvp.Key)
                .ToHashSet()
            : new HashSet<Guid>();

        var dailyByStall = includeFish
            ? await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId) && dc.IsPaid
                    && !stallsWithNpmPeriodPayments.Contains(dc.StallId)
                    && dc.CollectionDate >= complianceStart && dc.CollectionDate <= complianceEnd)
                .GroupBy(dc => dc.StallId)
                .Select(g => new { StallId = g.Key, Total = g.Sum(dc => dc.DailyFee) })
                .ToDictionaryAsync(x => x.StallId, x => x.Total, ct)
            : new Dictionary<Guid, decimal>();

        // Months (this year, up to the report month) in which each NPM stall recorded at least one
        // paid daily collection. NPM is collected daily, so a daily collection — not a monthly
        // "Paid" PaymentRecord — is the real evidence that a month was paid. Used by CountMissedMonths.
        var yearStart = new DateOnly(endDate.Year, 1, 1);
        var dailyPaidMonthsByStall = includeFish
            ? (await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => stallIds.Contains(dc.StallId) && dc.IsPaid
                        && dc.CollectionDate >= yearStart && dc.CollectionDate <= endDate)
                    .Select(dc => new { dc.StallId, dc.CollectionDate })
                    .ToListAsync(ct))
                .GroupBy(x => x.StallId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.CollectionDate.Month).ToHashSet())
            : new Dictionary<Guid, HashSet<int>>();

        // Excused/absent dates per stall (this year, up to the report month) — used to drop absent days
        // out of the NPM obligation and to skip fully-absent months in the missed-months count.
        var absentDatesByStall = includeFish
            ? (await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => stallIds.Contains(dc.StallId) && dc.IsAbsent
                        && dc.CollectionDate >= yearStart && dc.CollectionDate <= endDate)
                    .Select(dc => new { dc.StallId, dc.CollectionDate })
                    .ToListAsync(ct))
                .GroupBy(x => x.StallId)
                .ToDictionary(g => g.Key, g => (IReadOnlySet<DateOnly>)g.Select(x => x.CollectionDate).ToHashSet())
            : new Dictionary<Guid, IReadOnlySet<DateOnly>>();

        // Facility-wide NPM market closures in the window excuse EVERY NPM payor for those dates — they
        // are merged into each stall's absent set so the day owes ₱0 and never counts as missed.
        var marketClosedDates = includeFish
            ? (await _context.NpmMarketClosures
                    .AsNoTracking()
                    .Where(c => c.ClosureDate >= yearStart && c.ClosureDate <= endDate)
                    .Select(c => c.ClosureDate)
                    .ToListAsync(ct))
                .ToHashSet()
            : new HashSet<DateOnly>();

        // Admin-excused months for monthly facilities (TCC/NCC/BBQ/ICE) overlapping the period — these
        // months are ₱0 owed and never count as unpaid/missed/delinquent.
        var excusedByStall = !includeFish
            ? (await _context.StallMonthlyExceptions
                    .AsNoTracking()
                    .Where(e => stallIds.Contains(e.StallId)
                        && (e.BillingYear > complianceStart.Year || (e.BillingYear == complianceStart.Year && e.BillingMonth >= complianceStart.Month))
                        && (e.BillingYear < complianceEnd.Year || (e.BillingYear == complianceEnd.Year && e.BillingMonth <= complianceEnd.Month)))
                    .Select(e => new { e.StallId, e.BillingYear, e.BillingMonth })
                    .ToListAsync(ct))
                .GroupBy(e => e.StallId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlySet<(int Year, int Month)>)g.Select(x => (x.BillingYear, x.BillingMonth)).ToHashSet())
            : new Dictionary<Guid, IReadOnlySet<(int Year, int Month)>>();

        var rows = new List<StallComplianceDto>();

        foreach (var s in stalls)
        {
            // The lessee ANSWERABLE for this period, not merely the one sitting there now. On a stall that has
            // changed hands, the later occupancy answers for the period it covered — so a past month reports the
            // person who actually held the stall then, with their own rate. Monthly charges are one indivisible
            // obligation per stall-month in this system, so the occupancy that covered the period's end answers
            // for it; a mid-period handover on a daily-billed stall splits naturally by collection date.
            var occupancy = s.OccupanciesOverlapping(startDate, endDate, PhilippineTime.Today)
                .OrderByDescending(o => o.Start)
                .FirstOrDefault();
            var contract = occupancy?.Contract ?? s.Contracts.FirstOrDefault(c => c.IsActive);
            // NPM absent set = this stall's own absent days ∪ facility-wide market closures.
            IReadOnlySet<DateOnly>? absentSet = null;
            if (includeFish)
            {
                var union = new HashSet<DateOnly>(marketClosedDates);
                if (absentDatesByStall.GetValueOrDefault(s.Id) is { } perStall)
                    union.UnionWith(perStall);
                absentSet = union;
            }
            var excusedSet = includeFish ? null : excusedByStall.GetValueOrDefault(s.Id);
            IReadOnlySet<int>? excusedMonthsThisYear = excusedSet is null
                ? null
                : excusedSet.Where(t => t.Year == endDate.Year).Select(t => t.Month).ToHashSet();

            decimal totalBill;
            decimal rentBill;
            string? orNumber = null;
            decimal amountPaid;

            // For NPM, the monthly record is the monthly equivalent of a daily ₱30 obligation.
            // The compliance balance is always selected-period obligation minus selected-period collections.
            if (includeFish)
            {
                var npmPayments = periodPayments.GetValueOrDefault(s.Id) ?? new List<PaymentRecord>();
                // Occupancy-prorated rent: collectable days in the period × ₱30 (counts from the
                // contract's effectivity date, so a payor who started mid-month owes only their days).
                rentBill = CalculateNpmDailyObligation(s, complianceStart, complianceEnd, absentSet);
                totalBill = rentBill
                    + npmPayments.Sum(pr => CalculateNpmAdditionalCharges(pr, complianceStart, complianceEnd));
                amountPaid = npmPayments.Sum(pr => RecognizedNpmPaymentRevenue(pr, complianceStart, complianceEnd, s))
                    + dailyByStall.GetValueOrDefault(s.Id);
                orNumber = npmPayments
                    .Where(pr => !string.IsNullOrWhiteSpace(pr.ORNumber))
                    .OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1))
                    .Select(pr => pr.ORNumber)
                    .FirstOrDefault();
            }
            else if (periodPayments.TryGetValue(s.Id, out var payments) && payments.Count > 0)
            {
                // Monthly-billed facilities (TCC/NCC/BBQ/ICE): the bill is the FULL rent obligation
                // due across every month the contract is effective in the period (so unpaid months
                // without a record still count), plus any utilities actually billed on in-period
                // records. Recorded months are billed at THEIR snapshot rate (history-faithful across
                // rate changes); only unrecorded due months use the stall's current rate.
                rentBill = CalculateMonthlyRentObligationDue(s, complianceStart, complianceEnd, payments, excusedSet);
                totalBill = rentBill
                    + payments.Sum(pr => (pr.ElecAmount ?? 0) + (pr.WaterAmount ?? 0));
                amountPaid = payments.Sum(pr => pr.Status == PaymentStatus.Paid
                    ? pr.BaseRentalAmount + (pr.ElecAmount ?? 0) + (pr.WaterAmount ?? 0)
                    : pr.Status == PaymentStatus.Partial ? pr.PartialAmount : 0m);
                orNumber = payments
                    .Where(pr => !string.IsNullOrWhiteSpace(pr.ORNumber))
                    .OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1))
                    .Select(pr => pr.ORNumber)
                    .FirstOrDefault();
            }
            else
            {
                // No payment record in the period. Monthly facilities still owe the full rent
                // obligation that has come due across the period (every effective, started month —
                // not just one). NPM has no monthly record here either, so its daily collections
                // (dailyByStall) settle against its own daily obligation.
                rentBill = includeFish
                    ? CalculateNpmDailyObligation(s, complianceStart, complianceEnd, absentSet)
                    : CalculateStallRentObligationDue(s, complianceStart, complianceEnd, excusedSet);
                totalBill = rentBill;
                amountPaid = dailyByStall.GetValueOrDefault(s.Id);
            }

            var balance = Math.Max(0m, totalBill - amountPaid);

            // Distinct "Absent" status: the stall WAS under contract this period (had collectable days)
            // but every one of them was excused/absent, so nothing is owed and nothing was paid.
            var absentDays = absentSet is null
                ? 0
                : absentSet.Count(d => d >= complianceStart && d <= complianceEnd && IsStallCollectableOn(s, d));
            var hadRawCollectableDays = includeFish && CountNpmCollectableDays(s, complianceStart, complianceEnd) > 0;
            var allDaysExcused = includeFish && absentDays > 0 && rentBill <= 0m && amountPaid <= 0m && hadRawCollectableDays;

            // Monthly stall whose every due month in the period is admin-excused (raw obligation > 0,
            // but the excused-adjusted obligation is 0 and nothing was paid) → distinct "Excused".
            var monthlyFullyExcused = !includeFish && amountPaid <= 0m && rentBill <= 0m
                && CalculateStallRentObligationDue(s, complianceStart, complianceEnd, null) > 0m;

            var status = allDaysExcused
                ? "Absent"
                : monthlyFullyExcused
                    ? "Excused"
                    : balance <= 0m ? "Paid" : amountPaid > 0m ? "Partial" : "Unpaid";

            var missedMonths = CountMissedMonths(
                paymentRecords, s, endDate, includeFish, dailyPaidMonthsByStall.GetValueOrDefault(s.Id), absentSet,
                excusedMonthsThisYear, countMissedFrom);

            rows.Add(new StallComplianceDto(
                s.Id,
                s.StallNo,
                contract?.ActualOccupant ?? string.Empty,
                contract?.NameOnContract ?? string.Empty,
                s.Section.HasValue ? SectionLabel(s.Section) : (s.CustomSectionName ?? s.AreaLocation?.ToString() ?? string.Empty),
                s.Type.ToString(),
                s.MonthlyRate,
                // NPM bills per day through Stall.ResolveDailyFee, so report the fee this stall is actually
                // charged: a custom section its own rate, a canonical stall the tenant's resolved rate (which
                // fixes legacy NPM stalls that stored the old ₱30 default). Non-NPM keeps its stored per-stall
                // daily rate. Cantilan is unchanged (stored == resolved == ₱30).
                includeFish ? s.ResolveDailyFee(_npmDailyRate) : (s.DailyRate ?? 0m),
                status,
                amountPaid,
                balance,
                orNumber,
                missedMonths,
                s.AreaSqm ?? 0,
                contract?.EffectivityDate,
                contract?.DurationYears ?? 0,
                rentBill,
                absentDays));
        }

        return rows.OrderBy(r => NaturalStallSortKey(r.StallNo), StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Shared delinquency source (dashboard + Financial Reports): occupied stalls behind on payments
    /// over the rolling 12-month window ending at, and EXCLUDING, the given month. Counts unpaid/partial
    /// billing months and sums their balance due (cumulative). Optionally scoped to one facility.
    /// </summary>
    public async Task<IReadOnlyList<DelinquentStallDto>> GetDelinquentStallsAsync(
        FacilityCode? facility, int year, int month, CancellationToken ct)
        => await GetDelinquentStallsAsync(facility, year, month, includeClosed: false, ct);

    /// <summary>
    /// Who is behind, and by how many months.
    ///
    /// <para>Counted on the SAME rules as the compliance rows the office reads per stall
    /// (<see cref="GenerateStallComplianceAsync"/> → <see cref="CountMissedMonths"/>): every fully-elapsed month
    /// the stall was under contract and owed something, unless it was covered — a fully-paid monthly record, or
    /// for NPM a month settled by its daily collections. Months outside the contract, admin-excused months, and
    /// months whose every collectable day was an absence or a market closure are not owed and never counted.</para>
    ///
    /// <para>It used to count only months that HAD a PaymentRecord with a status other than Paid. Nothing writes
    /// such a row until money is recorded, so a payor who simply never paid last month counted as zero months
    /// behind and appeared on neither the Financial Reports' arrears list nor the dashboard's overdue list — while
    /// the same stall's compliance row showed the month as missed. One rule now answers both.</para>
    ///
    /// <para><paramref name="includeClosed"/> is kept for its callers but no longer changes the outcome: freezing a
    /// stall ends its obligation (the platform's rule everywhere — see <c>IsStallCollectableOn</c>), so a closed
    /// stall reports no missed months here or in its own compliance row. What a closed account still owes is
    /// reported by the Closed / Inactive Accounts register, which states its uncollected balance.</para>
    /// </summary>
    public async Task<IReadOnlyList<DelinquentStallDto>> GetDelinquentStallsAsync(
        FacilityCode? facility, int year, int month, bool includeClosed, CancellationToken ct)
    {
        var facilities = await _context.Facilities
            .AsNoTracking()
            .Select(f => new { f.Id, f.Code })
            .ToListAsync(ct);

        var targets = facility.HasValue
            ? facilities.Where(f => f.Code == facility.Value).ToList()
            : facilities;

        if (targets.Count == 0)
            return Array.Empty<DelinquentStallDto>();

        // The span the count is about: whole months that have already elapsed, ending the day before the anchor
        // month begins. A month still underway is never "missed" — a payor is not in arrears for a month they can
        // still pay — and neither is a month that has not arrived, so a future anchor (the Yearly view offers
        // every month) is clamped to the last month that has actually ended. Without the clamp the balance
        // included the month in progress while the count did not, and the row read "7 months" beside eight
        // months of money.
        var today = PhilippineTime.Today;
        var lastElapsed = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        var end = new DateOnly(year, month, 1).AddDays(-1);
        if (end > lastElapsed) end = lastElapsed;

        // Twelve months back, crossing the year boundary. Counting from January of the end year would have said a
        // payor who last paid in October was one month behind on the first of February.
        var start = new DateOnly(end.Year, end.Month, 1).AddMonths(-11);
        if (end < start) return Array.Empty<DelinquentStallDto>();

        // The tenant's own NPM rates. Every other entry point loads them; this one relied on a sibling report
        // having run first on the same scoped instance, which held only because Cantilan's rates are the
        // platform's constants.
        await LoadNpmRatesAsync(end, ct);

        // Freezing a stall ends its obligation, so a closed stall has no missed months to report either way; the
        // flag is honoured only in that it never widens the list. Kept explicit so the intent is not mistaken for
        // an oversight.
        var closedStallIds = (await _context.Stalls.AsNoTracking()
                .Where(s => s.Status != StallStatus.Active)
                .Select(s => s.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var results = new List<DelinquentStallDto>();
        foreach (var target in targets)
        {
            var compliance = await GenerateStallComplianceAsync(target.Code, target.Id, start, end, ct, countMissedFrom: start);

            results.AddRange(compliance
                .Where(r => r.MissedMonths >= 1 && !closedStallIds.Contains(r.StallId))
                .Select(r => new DelinquentStallDto(
                    target.Code, r.StallNo, r.Occupant, r.MissedMonths, r.Balance, r.StallId)));
        }

        return results
            .OrderByDescending(d => d.MonthsUnpaid)
            .ThenByDescending(d => d.OutstandingBalance)
            .ToList();
    }

    // Orders stall numbers naturally so "2" precedes "10" (and "A2" precedes "A10"): each run of
    // digits is zero-padded to a fixed width before an ordinal compare. Plain string ordering put
    // "10" before "2" across the reports' All Stalls / Status Report tables.
    private static string NaturalStallSortKey(string stallNo) =>
        string.IsNullOrEmpty(stallNo)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(stallNo, "[0-9]+", m => m.Value.PadLeft(12, '0'));

    /// <summary>
    /// Counts months (this year, up to the report month) in which the stall was under an active
    /// contract yet recorded no payment at all — the delinquency signal for the report page.
    /// <para>
    /// Two rules keep this honest:
    /// (1) Months before the contract's effectivity (or after expiry) are never counted — a stall
    ///     cannot be "behind" on a month it was not yet operating.
    /// (2) For NPM, which is collected daily, a month counts as paid if it has either a paid daily
    ///     collection OR a non-Unpaid monthly record. Without this, every NPM stall would read as
    ///     fully delinquent because daily payors rarely have monthly "Paid" records.
    /// Other facilities keep the monthly-billing rule: a month is missed unless it has a fully-Paid
    /// record (Partial still counts as behind, matching the dashboard delinquency definition).
    /// </para>
    /// </summary>
    private static int CountMissedMonths(
        List<PaymentRecord> paymentRecords,
        Stall stall,
        DateOnly endDate,
        bool isNpm,
        HashSet<int>? dailyPaidMonths,
        IReadOnlySet<DateOnly>? absentDates = null,
        IReadOnlySet<int>? excusedMonths = null,
        DateOnly? countFrom = null)
    {
        dailyPaidMonths ??= new HashSet<int>();

        // Where the walk starts. The compliance column counts this year, so it passes nothing and gets January;
        // the arrears/delinquency source hands in a year-crossing span, because a payor who last paid in October
        // of the previous year is not "one month behind" the moment January turns over.
        var first = countFrom is { } from && from < new DateOnly(endDate.Year, endDate.Month, 1)
            ? new DateOnly(from.Year, from.Month, 1)
            : new DateOnly(endDate.Year, 1, 1);

        var stallPayments = paymentRecords
            .Where(pr => pr.StallId == stall.Id
                && (pr.BillingYear > first.Year || (pr.BillingYear == first.Year && pr.BillingMonth >= first.Month))
                && (pr.BillingYear < endDate.Year || (pr.BillingYear == endDate.Year && pr.BillingMonth <= endDate.Month)))
            .ToList();

        // Months with a fully-Paid record (non-NPM "covered" rule).
        var paidMonths = stallPayments
            .Where(pr => pr.Status == PaymentStatus.Paid)
            .Select(pr => (pr.BillingYear, pr.BillingMonth))
            .ToHashSet();

        // Months with any non-Unpaid record (NPM "paid something" rule).
        var settledMonths = stallPayments
            .Where(pr => pr.Status != PaymentStatus.Unpaid)
            .Select(pr => (pr.BillingYear, pr.BillingMonth))
            .ToHashSet();

        var missed = 0;
        var today = PhilippineTime.Today;
        for (var cursor = first; cursor <= new DateOnly(endDate.Year, endDate.Month, 1); cursor = cursor.AddMonths(1))
        {
            var monthStart = cursor;
            var monthEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));

            // Count only fully-elapsed PAST months. The current, in-progress month is never "missed"
            // yet (a payor is not in arrears for a month still underway), and future months are not due
            // (e.g. the Yearly view runs to December). Arrears/delinquency count from past months only.
            if (monthEnd >= today)
                continue;

            // Skip months the stall was not under an active contract (pre-effectivity / post-expiry),
            // or months whose every collectable day was excused/absent (nothing was owed → not missed).
            if (CountNpmCollectableDays(stall, monthStart, monthEnd, absentDates) == 0)
                continue;

            // Admin-excused monthly months are not owed → never missed. The excused set is this year's, so it
            // only applies to months of the year the caller asked about.
            if (!isNpm && excusedMonths is not null && cursor.Year == endDate.Year && excusedMonths.Contains(cursor.Month))
                continue;

            var covered = isNpm
                ? settledMonths.Contains((cursor.Year, cursor.Month))
                    || (cursor.Year == endDate.Year && dailyPaidMonths.Contains(cursor.Month))
                : paidMonths.Contains((cursor.Year, cursor.Month));

            if (!covered)
                missed++;
        }

        return missed;
    }

    private static string SectionLabel(MarketSection? section) => section switch
    {
        MarketSection.VegetableArea => "Vegetable Area",
        MarketSection.FishSection => "Fish Area",
        MarketSection.MeatSection => "Meat Area",
        _ => string.Empty
    };

    #endregion

}
