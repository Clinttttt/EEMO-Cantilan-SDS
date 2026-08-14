using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
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

        // The span this build needs records for: the reporting period, and — when the caller counts a longer
        // arrears window — back to the first month of that count. Loading every payment record a stall has ever
        // had was the heaviest query here, and none of it outside this span is read.
        var countStart = countMissedFrom is { } cf && cf < new DateOnly(endDate.Year, endDate.Month, 1)
            ? new DateOnly(cf.Year, cf.Month, 1)
            : new DateOnly(endDate.Year, 1, 1);
        var recordsFrom = countStart < startDate ? countStart : new DateOnly(startDate.Year, startDate.Month, 1);

        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => stallIds.Contains(pr.StallId)
                && (pr.BillingYear > recordsFrom.Year
                    || (pr.BillingYear == recordsFrom.Year && pr.BillingMonth >= recordsFrom.Month))
                && (pr.BillingYear < endDate.Year
                    || (pr.BillingYear == endDate.Year && pr.BillingMonth <= endDate.Month)))
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

        var dailyRowsByStall = includeFish
            ? (await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId) && dc.IsPaid
                    && !stallsWithNpmPeriodPayments.Contains(dc.StallId)
                    && dc.CollectionDate >= complianceStart && dc.CollectionDate <= complianceEnd)
                .Select(dc => new { dc.StallId, dc.CollectionDate, dc.DailyFee })
                .ToListAsync(ct))
                .GroupBy(x => x.StallId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => (x.CollectionDate, x.DailyFee)).ToList())
            : new Dictionary<Guid, List<(DateOnly CollectionDate, decimal DailyFee)>>();

        // What each NPM stall actually collected in each month of the counted span. NPM is collected daily, so the
        // evidence a month was settled is money — the daily fees plus any month-end adjustment — measured against
        // that month's obligation. Counting "any paid day" as settled let a stall that paid ₱30 of a ₱900 month
        // read as fully covered while ₱870 was still owed.
        var yearStart = countStart;
        var dailyCollectedByStallMonth = includeFish
            ? (await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => stallIds.Contains(dc.StallId) && dc.IsPaid
                        && dc.CollectionDate >= yearStart && dc.CollectionDate <= endDate)
                    .Select(dc => new { dc.StallId, dc.CollectionDate, dc.DailyFee, dc.MonthEndAdjustment })
                    .ToListAsync(ct))
                .GroupBy(x => x.StallId)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(x => (x.CollectionDate.Year, x.CollectionDate.Month))
                          .ToDictionary(m => m.Key, m => m.Sum(x => x.DailyFee + (x.MonthEndAdjustment ?? 0m))))
            : new Dictionary<Guid, Dictionary<(int Year, int Month), decimal>>();

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
            var occupancy = s.OccupanciesOverlapping(startDate, endDate, _clock.PhilippineToday)
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
            // Every excused (year, month), not just the anchor year's. The count used to filter these to the end
            // year while the BALANCE beside it forgave the whole set, so across a year boundary a stall read more
            // months behind than its money — and could be promoted from arrears to delinquent on the strength of a
            // month the office had already excused.
            IReadOnlySet<(int Year, int Month)>? excusedMonths = excusedSet;

            // Where THIS stall's present account begins. A stall that was re-let (or renewed onto a new contract)
            // carries the earlier occupancy's months in the Closed / Inactive register, which states that account's
            // own uncollected balance under the lessee who held it. Billing those same months again on the current
            // row reported the debt twice and put it under the wrong name: stall 23's row read twelve months and
            // ₱10,050 on a contract that began five weeks earlier, ₱9,210 of it already in the register.
            // A stall let once — including one whose term has lapsed while the tenant trades on — is unaffected:
            // its only occupancy starts where it always did, and it stays in the collection lists.
            var accountStart = CurrentAccountStart(s, complianceStart, complianceEnd);

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
                rentBill = CalculateNpmDailyObligation(s, accountStart, complianceEnd, absentSet);
                totalBill = rentBill
                    + npmPayments.Sum(pr => CalculateNpmAdditionalCharges(pr, accountStart, complianceEnd));
                amountPaid = npmPayments.Sum(pr => RecognizedNpmPaymentRevenue(pr, accountStart, complianceEnd, s))
                    + DailyCollectedFrom(dailyRowsByStall, s.Id, accountStart);
                orNumber = npmPayments
                    .Where(pr => !string.IsNullOrWhiteSpace(pr.ORNumber))
                    .OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1))
                    .Select(pr => pr.ORNumber)
                    .FirstOrDefault();
            }
            else if (periodPayments.TryGetValue(s.Id, out var allPayments) && allPayments.Count > 0)
            {
                // Only records from the present account onwards: a previous occupancy's payments are credited to
                // that occupancy by the register, and crediting them here as well would settle the sitting
                // lessee's rent with money someone else paid.
                var payments = allPayments
                    .Where(pr => pr.BillingYear > accountStart.Year
                        || (pr.BillingYear == accountStart.Year && pr.BillingMonth >= accountStart.Month))
                    .ToList();
                // Monthly-billed facilities (TCC/NCC/BBQ/ICE): the bill is the FULL rent obligation
                // due across every month the contract is effective in the period (so unpaid months
                // without a record still count), plus any utilities actually billed on in-period
                // records. Recorded months are billed at THEIR snapshot rate (history-faithful across
                // rate changes); only unrecorded due months use the stall's current rate.
                rentBill = CalculateMonthlyRentObligationDue(s, accountStart, complianceEnd, payments, excusedSet);
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
                    ? CalculateNpmDailyObligation(s, accountStart, complianceEnd, absentSet)
                    : CalculateStallRentObligationDue(s, accountStart, complianceEnd, excusedSet);
                totalBill = rentBill;
                amountPaid = DailyCollectedFrom(dailyRowsByStall, s.Id, accountStart);
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
                paymentRecords, s, endDate, includeFish, dailyCollectedByStallMonth.GetValueOrDefault(s.Id), absentSet,
                excusedMonths, countMissedFrom, NewestOccupancyStart(s, complianceEnd));

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
        => await GetDelinquentStallsAsync(facility, year, month, includeClosed, wholeAccount: false, ct);

    /// <summary>
    /// <paramref name="wholeAccount"/> chooses the span. False counts a rolling twelve months to the anchor, which
    /// is what a period-scoped screen must state: a row headed "January – December 2026" cannot carry a count of
    /// thirty-seven months. True counts from where each account itself began, which is what the Financial Reports
    /// state so their figures agree with the register and the whole-time history.
    /// </summary>
    public async Task<IReadOnlyList<DelinquentStallDto>> GetDelinquentStallsAsync(
        FacilityCode? facility, int year, int month, bool includeClosed, bool wholeAccount, CancellationToken ct)
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

        // The span the count is about: every month of each account that has already closed. A month still underway
        // is never "missed" — a payor is not in arrears for a month they can still pay — and neither is a month that
        // has not arrived, so a future anchor (the Yearly view offers every month) is clamped to the last month that
        // has actually ended.
        //
        // It used to be a rolling twelve months, which made the Financial Reports state ₱9,900 for an account the
        // register and the whole-time Follow-up History both stated at ₱33,300: the same debt, silently truncated,
        // and the smaller figure is the one that reaches a demand letter. The walk now begins at the tenant's first
        // year of activity and each stall's own account start does the rest of the clamping, so a row states its
        // whole outstanding — 37 months where 37 months are owed. There is no year boundary left to fall over.
        var today = _clock.PhilippineToday;
        var lastElapsed = new DateOnly(today.Year, today.Month, 1).AddDays(-1);
        var end = new DateOnly(year, month, 1).AddDays(-1);
        if (end > lastElapsed) end = lastElapsed;

        var start = wholeAccount
            ? new DateOnly(await GetEarliestActivityYearAsync(ct), 1, 1)
            // A rolling twelve months, crossing the year boundary. Counting from January of the end year would say a
            // payor who last paid in October was one month behind on the first of February.
            : new DateOnly(end.Year, end.Month, 1).AddMonths(-11);
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

        // Accounts whose term has run out while the space was never handed over. The office is still collecting
        // from them — that is why they are on this list — but the row should say so, or a lapsed tenancy is
        // indistinguishable from a live one and the office cannot tell whether to renew or to chase. One query for
        // the tenant, not one per facility.
        var lapsedStallIds = (await _context.Stalls
                .AsNoTracking()
                .Include(s => s.Contracts)
                .Where(s => s.Status == StallStatus.Active && s.Contracts.Any())
                .ToListAsync(ct))
            .Where(s => s.Occupancies(today).LastOrDefault() is { } newest
                && !newest.IsCurrent
                && newest.Contract.EndedOn is null)
            .Select(s => s.Id)
            .ToHashSet();

        var results = new List<DelinquentStallDto>();
        foreach (var target in targets)
        {
            var compliance = await GenerateStallComplianceAsync(target.Code, target.Id, start, end, ct, countMissedFrom: start);

            results.AddRange(compliance
                .Where(r => r.MissedMonths >= 1 && !closedStallIds.Contains(r.StallId))
                .Select(r => new DelinquentStallDto(
                    target.Code, r.StallNo, r.Occupant, r.MissedMonths, r.Balance, r.StallId,
                    TermLapsed: lapsedStallIds.Contains(r.StallId),
                    // The market numbers its spaces per section, so "Stall 1" exists in the Vegetable Area, the
                    // Fish Section and the Meat Section at once — three different payors. Without the section the
                    // office cannot tell which space a row is about.
                    Section: r.Section)));
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
    /// The daily fees collected for a stall from <paramref name="from"/> onwards. Money collected before the
    /// present account began belongs to the previous occupancy, which the Closed / Inactive register credits
    /// under its own lessee — crediting it here as well would pay the current account's rent twice.
    /// </summary>
    private static decimal DailyCollectedFrom(
        Dictionary<Guid, List<(DateOnly CollectionDate, decimal DailyFee)>> rowsByStall,
        Guid stallId,
        DateOnly from)
        => rowsByStall.TryGetValue(stallId, out var rows)
            ? rows.Where(r => r.CollectionDate >= from).Sum(r => r.DailyFee)
            : 0m;

    /// <summary>
    /// Where a stall's PRESENT account begins, inside the period being reported.
    /// <para>
    /// A stall that was re-let or renewed onto a new contract has its earlier occupancy reported by the Closed /
    /// Inactive register, which states that account's own uncollected balance under the lessee who held it.
    /// Counting those months again on the stall's current row reports the same debt twice and files it under the
    /// wrong name. So the current row starts at the newest occupancy's effectivity, or at the period start,
    /// whichever is later.
    /// </para>
    /// <para>
    /// A stall let only once is unchanged — including one whose term has lapsed while the tenant trades on, whose
    /// single occupancy starts where it always did. Those accounts stay in the arrears and delinquency lists,
    /// because the office is still collecting from them.
    /// </para>
    /// </summary>
    private static DateOnly CurrentAccountStart(Stall stall, DateOnly periodStart, DateOnly asOf)
    {
        var newest = NewestOccupancyStart(stall, asOf);
        return newest is { } start && start > periodStart ? start : periodStart;
    }

    /// <summary>
    /// The effectivity of the newest occupancy on this stall, irrespective of the period being reported. The
    /// missed-month count walks a span of its own (this year, or a rolling twelve months), so it must be bounded
    /// by where the present account began and not by the period start.
    /// </summary>
    private static DateOnly? NewestOccupancyStart(Stall stall, DateOnly asOf)
        => stall.Occupancies(asOf).LastOrDefault()?.Start;

    /// <summary>
    /// Counts the months in the span in which the stall owed rent and the month was not settled — the figure
    /// behind "months behind" on the reports, the arrears/delinquency lists and the dashboard.
    /// <para>
    /// The rules:
    /// (1) Months before the contract's effectivity (or after expiry) are never counted — a stall cannot be
    ///     "behind" on a month it was not yet operating — nor are months whose every collectable day was an
    ///     absence or a market closure, or an admin-excused month: nothing was owed.
    /// (2) A month is settled when its OBLIGATION is settled, not when something was paid towards it. For NPM,
    ///     collected daily, that means the month's daily fees (with any month-end adjustment) reach the month's
    ///     contractual rent; ₱30 against a ₱900 month leaves the month outstanding and ₱870 owed. A fully-paid
    ///     monthly record also settles it, for a payor who paid the month in one go.
    /// (3) Other facilities keep the monthly-billing rule: a month is missed unless it has a fully-Paid record,
    ///     so a partial payment reduces the balance and leaves the month outstanding.
    /// </para>
    /// </summary>
    private int CountMissedMonths(
        List<PaymentRecord> paymentRecords,
        Stall stall,
        DateOnly endDate,
        bool isNpm,
        Dictionary<(int Year, int Month), decimal>? dailyCollectedByMonth,
        IReadOnlySet<DateOnly>? absentDates = null,
        IReadOnlySet<(int Year, int Month)>? excusedMonths = null,
        DateOnly? countFrom = null,
        DateOnly? accountStart = null)
    {
        dailyCollectedByMonth ??= new Dictionary<(int Year, int Month), decimal>();

        // Where the walk starts. The compliance column counts this year, so it passes nothing and gets January;
        // the arrears/delinquency source hands in a year-crossing span, because a payor who last paid in October
        // of the previous year is not "one month behind" the moment January turns over.
        var first = countFrom is { } from && from < new DateOnly(endDate.Year, endDate.Month, 1)
            ? new DateOnly(from.Year, from.Month, 1)
            : new DateOnly(endDate.Year, 1, 1);

        // Never before the present account began: a superseded occupancy's months are the register's to report.
        if (accountStart is { } acct && new DateOnly(acct.Year, acct.Month, 1) > first)
            first = new DateOnly(acct.Year, acct.Month, 1);

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

        var missed = 0;
        var today = _clock.PhilippineToday;
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

            // And, for a monthly-billed space, only the term's own billing months. A term of N years owes N × 12,
            // so the anniversary month is not a further month — counting it left the row stating one month more than
            // the balance beside it.
            if (!isNpm && !stall.Contracts.Any(c => c.IsActive && c.BillsCalendarMonth(cursor.Year, cursor.Month)))
                continue;

            // An admin-excused month owes nothing, in whatever year it falls. Restricting this to the anchor year
            // made the count disagree with the balance beside it across a year boundary.
            if (!isNpm && excusedMonths is not null && excusedMonths.Contains((cursor.Year, cursor.Month)))
                continue;

            // A month is settled when its obligation is settled. For NPM that is money against the month's
            // contractual rent — the daily fees collected, with any month-end adjustment — or a monthly record
            // paid in full for a payor who settled the month in one go. Anything short of it leaves the month
            // outstanding, which is what the balance beside it already says.
            var covered = isNpm
                ? paidMonths.Contains((cursor.Year, cursor.Month))
                    || dailyCollectedByMonth.GetValueOrDefault((cursor.Year, cursor.Month))
                        >= CalculateNpmDailyObligation(stall, monthStart, monthEnd, absentDates)
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
