using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Extensions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of PaymentRepository: ONE account's position (IStallLedgerQueries) - its payment history, its summary, the months it
// still owes, and its collection history.
//
// These carry the obligation arithmetic that decides how much of a month an occupancy owes, including the collectable-day count
// they all share. It stays here, in one file, with the reads that use it: the office reconciles a stallholder's ledger against
// its own reports by hand, and two copies of this arithmetic would be two answers to the same question.
public partial class PaymentRepository
{
    public async Task<IReadOnlyList<PaymentHistoryDto>> GetPaymentHistoryAsync(Guid stallId, CancellationToken ct)
    {
        var now = _clock.PhilippineNow;
        var startDate = now.AddMonths(-11);

        var stall = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .FirstOrDefaultAsync(s => s.Id == stallId, ct);

        var payments = await _context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.StallId == stallId)
            .Where(p => (p.BillingYear > startDate.Year) || (p.BillingYear == startDate.Year && p.BillingMonth >= startDate.Month))
            .ToListAsync(ct);

        // Non-NPM facilities are billed monthly — the payment record is the source of truth.
        if (stall?.Facility?.Code != FacilityCode.NPM)
        {
            // Display-only recorder attribution: field collector when set, else the admin/Head
            // captured in the audit actor (UpdatedBy ?? CreatedBy). Both lookups built once (no N+1).
            var monthlyCollectorNames = await LoadCollectorNamesAsync(payments.Select(p => p.CollectorId), ct);
            var monthlyAdminNames = await LoadAdminNamesAsync(payments.Select(p => p.UpdatedBy ?? p.CreatedBy), ct);

            return payments
                .OrderByDescending(p => p.BillingYear)
                .ThenByDescending(p => p.BillingMonth)
                .Select(p => new PaymentHistoryDto(
                    $"{p.BillingYear:0000}-{p.BillingMonth:00}",
                    p.Status, p.TotalBill, p.AmountPaid, p.BalanceDue, p.ORNumber, p.PaidAt, null,
                    RecordedByName: ResolveRecorderName(p.CollectorId, p.UpdatedBy ?? p.CreatedBy, monthlyCollectorNames, monthlyAdminNames)))
                .ToList();
        }

        // NPM is collected daily — fold each month's daily collections into the monthly ledger so
        // the history reflects reality (a stall paying ₱30/day is not "Unpaid" for the month).
        // Window runs to the end of the CURRENT month (not clamped to today) so days paid in
        // advance still count — this mirrors the daily collection calendar, which shows every
        // paid day of the month regardless of whether the date has arrived yet.
        var windowStart = new DateOnly(startDate.Year, startDate.Month, 1);
        var windowEnd = new DateOnly(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
        // Resolve the municipality's NPM rates (constant fallback → Cantilan unchanged); each month's
        // ₱/day obligation is resolved as of that month below.
        var rateSnapshot = await _feeRateResolver.GetSnapshotAsync(ct);
        var dailies = await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.StallId == stallId && dc.IsPaid
                && dc.CollectionDate >= windowStart && dc.CollectionDate <= windowEnd)
            .Select(dc => new { dc.CollectionDate, dc.DailyFee, dc.CollectorId, dc.ORNumber })
            .ToListAsync(ct);

        // Excused/absent dates — these days are not owed, so they reduce each month's ₱30/day bill
        // (and a month entirely absent becomes a ₱0 "Absent" row instead of an Unpaid one).
        var absentDates = (await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.StallId == stallId && dc.IsAbsent
                && dc.CollectionDate >= windowStart && dc.CollectionDate <= windowEnd)
            .Select(dc => dc.CollectionDate)
            .ToListAsync(ct))
            .ToHashSet();

        // NPM market closures (facility-wide) also excuse the day — fold them in so a closed day is
        // never billed/Unpaid in the history (mirrors the ledger summary + Financial Reports).
        var historyClosedDates = await _context.NpmMarketClosures
            .AsNoTracking()
            .Where(c => c.ClosureDate >= windowStart && c.ClosureDate <= windowEnd)
            .Select(c => c.ClosureDate)
            .ToListAsync(ct);
        absentDates.UnionWith(historyClosedDates);

        var collectorIds = dailies.Where(d => d.CollectorId.HasValue).Select(d => d.CollectorId!.Value)
            .Concat(payments.Where(p => p.CollectorId.HasValue).Select(p => p.CollectorId!.Value))
            .Distinct().ToList();
        var collectorNames = collectorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _context.CollectorUsers
                .Where(c => collectorIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullName ?? string.Empty, ct);

        var result = new List<PaymentHistoryDto>();
        for (var i = 11; i >= 0; i--)
        {
            var m = now.AddMonths(-i);
            int year = m.Year, month = m.Month;
            var period = $"{year:0000}-{month:00}";
            var monthStart = new DateOnly(year, month, 1);
            var calendarEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            // Two windows, deliberately different. The OBLIGATION stops at the days earned, so this grid states the
            // month in progress the same way the ledger card above it does. COLLECTIONS still range to the calendar
            // month end, because money the office has taken is never dropped — a vendor who paid a day in advance
            // must still see it. Same asymmetry the collector's report has always carried.
            var earnedEnd = DomainRules.EarnedThrough(calendarEnd, _clock.PhilippineToday);
            var monthEnd = calendarEnd;

            // NPM is let for a MONTHLY rent and collected in ₱30 installments, so the month's obligation is that
            // rent — ₱900 for a month held in full, whatever the calendar gave it — less the days nothing is owed
            // for. A flat monthly PaymentRecord never overrides the ledger; collected is the installments actually
            // received (plus any month-end adjustment recorded on the month's last one).
            var daysHeld = CountCollectableDays(stall, monthStart, earnedEnd);
            var daysForgiven = absentDates.Count(d => d >= monthStart && d <= earnedEnd);
            var fee = stall.ResolveDailyFee(rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, monthEnd));
            var obligation = DomainRules.DailyBilledMonthObligation(
                fee,
                stall.ResolveMonthlyRent(
                    rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, monthEnd),
                    rateSnapshot.Resolve(FeeRateKey.NpmMonthlyStall, monthEnd)),
                // The CALENDAR length, never the clamped end: a month held in full owes the month's rent, and passing
                // the elapsed-day count here would make every month look like a part month.
                calendarEnd.Day,
                daysHeld);
            var bill = obligation - DomainRules.DailyBilledMonthCredit(fee, obligation, daysHeld, daysForgiven);
            var monthDailies = dailies.Where(d => d.CollectionDate >= monthStart && d.CollectionDate <= monthEnd).ToList();
            var amountPaid = monthDailies.Sum(d => d.DailyFee);

            // Only emit a row for months with actual daily collections. Months with no payment are
            // intentionally omitted so the modal can render them correctly: pre-contract months are
            // greyed out as "N/A", and collectable-but-unpaid months show as Unpaid. Emitting a
            // zero row here would defeat the modal's before-contract detection.
            if (amountPaid <= 0m)
            {
                // Exception: a month that was under contract but fully excused (every collectable day
                // absent) is emitted as a distinct ₱0 "Absent" row — it is not owed, so it must not
                // fall through to the modal's Unpaid default.
                if (daysHeld > 0 && daysForgiven >= daysHeld)
                {
                    result.Add(new PaymentHistoryDto(
                        period, PaymentStatus.Paid, 0m, 0m, 0m, null, null, null, IsExcused: true));                }
                continue;
            }

            // Settled when the money covers what has been earned. Requiring bill > 0 read "Partial" on a month whose
            // obligation has not started to accrue but which the vendor has already paid into — the first day or two
            // of a month, paid ahead. Nothing owed and something paid is Paid, not part-paid.
            var status = amountPaid >= bill ? PaymentStatus.Paid : PaymentStatus.Partial;
            var balance = Math.Max(0m, bill - amountPaid);
            var last = monthDailies.OrderByDescending(d => d.CollectionDate).FirstOrDefault();

            result.Add(new PaymentHistoryDto(
                period,
                status,
                bill,
                amountPaid,
                balance,
                last?.ORNumber,
                last is not null ? last.CollectionDate.ToDateTime(TimeOnly.MinValue) : null,
                last?.CollectorId is Guid lcid && collectorNames.TryGetValue(lcid, out var ln) ? ln : null,
                RecordedByName: last?.CollectorId is Guid rcid && collectorNames.TryGetValue(rcid, out var rln) ? rln : null));
        }

        return result;
    }

    /// <summary>
    /// Rolling 12-month ledger totals for a stall, daily-aware for NPM. For each month the stall is
    /// under an effective contract: NPM always folds that month's paid daily collections against the
    /// contract-aware ₱30/day obligation (the flat monthly record is ignored); non-NPM facilities use
    /// a non-Unpaid monthly record when present, otherwise owe the full monthly rent. Mirrors
    /// <see cref="GetPaymentHistoryAsync"/> so the profile summary reconciles with the history grid
    /// and the reports.
    /// </summary>
    public async Task<StallLedgerSummaryDto> GetStallLedgerSummaryAsync(Guid stallId, CancellationToken ct)
    {
        var now = _clock.PhilippineNow;
        var startDate = now.AddMonths(-11);

        var stall = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            // Every term, so the summary can be bounded to ONE occupancy. A stall handed over mid-month otherwise
            // credits the sitting lessee with days the previous occupant paid for.
            .Include(s => s.Contracts)
            .FirstOrDefaultAsync(s => s.Id == stallId, ct);

        if (stall is null)
            return new StallLedgerSummaryDto(0, 0, 0m, 0m);

        // This panel is the account of whoever holds the stall now (or last held it) — the same reading every
        // collection screen means by "this stall".
        var occupancy = stall.ResolveOccupancy(null, _clock.PhilippineToday);
        if (occupancy is null)
            return new StallLedgerSummaryDto(0, 0, 0m, 0m);

        var payments = await _context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.StallId == stallId)
            .Where(p => (p.BillingYear > startDate.Year) || (p.BillingYear == startDate.Year && p.BillingMonth >= startDate.Month))
            .ToListAsync(ct);

        var isNpm = stall.Facility?.Code == FacilityCode.NPM;

        // Resolve the municipality's NPM rates (constant fallback → Cantilan unchanged); each month's
        // ₱/day obligation is resolved as of that month below.
        var rateSnapshot = await _feeRateResolver.GetSnapshotAsync(ct);

        var windowStart = new DateOnly(startDate.Year, startDate.Month, 1);
        var windowEnd = new DateOnly(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
        var dailies = isNpm
            ? await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => dc.StallId == stallId && dc.IsPaid
                    && dc.CollectionDate >= windowStart && dc.CollectionDate <= windowEnd)
                .Select(dc => new { dc.CollectionDate, dc.DailyFee })
                .ToListAsync(ct)
            : new();

        // Excused/absent dates reduce the NPM obligation (a fully-absent month is not owed at all).
        var absentDates = isNpm
            ? (await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => dc.StallId == stallId && dc.IsAbsent
                    && dc.CollectionDate >= windowStart && dc.CollectionDate <= windowEnd)
                .Select(dc => dc.CollectionDate)
                .ToListAsync(ct))
                .ToHashSet()
            : new HashSet<DateOnly>();

        // NPM market closures (facility-wide) also excuse the day — union them with per-stall absences.
        var marketClosedDates = isNpm
            ? (await _context.NpmMarketClosures
                .AsNoTracking()
                .Where(c => c.ClosureDate >= windowStart && c.ClosureDate <= windowEnd)
                .Select(c => c.ClosureDate)
                .ToListAsync(ct))
                .ToHashSet()
            : new HashSet<DateOnly>();
        var excusedDates = new HashSet<DateOnly>(absentDates);
        excusedDates.UnionWith(marketClosedDates);

        // Monthly facilities: months an admin excused (e.g. temporary closure) owe nothing.
        var excusedMonths = isNpm
            ? new HashSet<(int Year, int Month)>()
            : (await _context.StallMonthlyExceptions
                .AsNoTracking()
                .Where(e => e.StallId == stallId)
                .Select(e => new { e.BillingYear, e.BillingMonth })
                .ToListAsync(ct))
                .Select(e => (e.BillingYear, e.BillingMonth))
                .ToHashSet();

        int monthsPaid = 0, monthsUnpaid = 0;
        decimal totalCollected = 0m, totalOutstanding = 0m;

        for (var i = 11; i >= 0; i--)
        {
            var m = now.AddMonths(-i);
            int year = m.Year, month = m.Month;
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

            // Clamped to this occupancy: on a handover month only their own days count, in either direction.
            if (monthStart < occupancy.Start) monthStart = occupancy.Start;
            if (monthEnd > occupancy.BillableEnd) monthEnd = occupancy.BillableEnd;

            // And, for a daily-collected space, never past today: NPM rent accrues per market day, so on the fourth
            // of the month only four days have been earned. One rule, shared with the reports, the payment dialog,
            // the collector's report and the register — see DomainRules.EarnedThrough. Monthly-billed facilities are
            // untouched: their rent falls due when the month opens.
            if (isNpm) monthEnd = DomainRules.EarnedThrough(monthEnd, _clock.PhilippineToday);

            if (monthEnd < monthStart)
                continue;

            // Skip months the stall was not active / under an effective contract (not yet due).
            if (CountCollectableDays(stall, monthStart, monthEnd) == 0)
                continue;

            var rec = payments.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);

            if (isNpm)
            {
                // The month's contractual rent, less the days nothing is owed for. Excused/absent/market-closed days
                // are credits against the obligation; a month entirely credited is skipped (not paid, not unpaid).
                var npmExcused = excusedDates.Count(d => d >= monthStart && d <= monthEnd);
                var daysHeld = CountCollectableDays(stall, monthStart, monthEnd);
                var fee = stall.ResolveDailyFee(rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, monthEnd));
                var obligation = DomainRules.DailyBilledMonthObligation(
                    fee,
                    stall.ResolveMonthlyRent(
                        rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, monthEnd),
                        rateSnapshot.Resolve(FeeRateKey.NpmMonthlyStall, monthEnd)),
                    // The CALENDAR length of the month, never the clamped end: monthEnd was narrowed to this
                    // occupancy's own last day above, and passing that would make a seven-day part month look like
                    // a month held in full — charging its whole rent for seven days.
                    DateTime.DaysInMonth(year, month),
                    daysHeld);
                var bill = obligation - DomainRules.DailyBilledMonthCredit(fee, obligation, daysHeld, npmExcused);
                if (bill <= 0m)
                    continue;
                var paid = dailies.Where(d => d.CollectionDate >= monthStart && d.CollectionDate <= monthEnd).Sum(d => d.DailyFee);
                totalCollected += paid;
                totalOutstanding += Math.Max(0m, bill - paid);
                if (paid >= bill) monthsPaid++; else monthsUnpaid++;
                continue;
            }

            // Non-NPM: an admin-excused month owes nothing. Any payment already made still counts as
            // collected (money received is never dropped); the month just adds no outstanding.
            if (excusedMonths.Contains((year, month)))
            {
                if (rec is not null && rec.Status != PaymentStatus.Unpaid)
                    totalCollected += rec.AmountPaid;
                continue;
            }

            // Non-NPM: a recorded (non-Unpaid) monthly payment is authoritative.
            if (rec is not null && rec.Status != PaymentStatus.Unpaid)
            {
                totalCollected += rec.AmountPaid;
                totalOutstanding += rec.BalanceDue;
                if (rec.Status == PaymentStatus.Paid) monthsPaid++; else monthsUnpaid++;
                continue;
            }

            // Monthly-billed facility with no record this month → full rent owed.
            totalOutstanding += stall.MonthlyRate;
            monthsUnpaid++;
        }

        return new StallLedgerSummaryDto(monthsPaid, monthsUnpaid, totalCollected, totalOutstanding);
    }

    /// <summary>
    /// The unpaid billing months of ONE occupancy. A stall outlives its lessees, so "the stall's arrears" is not a
    /// well-formed question on a stall that has been re-let: the sitting lessee and a previous one each have their
    /// own months, at their own rates.
    ///
    /// <para>Whose is decided in this order: the term named by <paramref name="contractId"/>; failing that, the term
    /// that held the stall during <paramref name="forPeriod"/> — which is what a screen showing a past period must
    /// be answered by; failing that, the most recent term, which is what every current collection screen means.</para>
    ///
    /// <para>Every figure is bounded to that occupancy's own window, so no lessee is ever billed for a day another
    /// held the stall, and the total agrees with the figure the inactive-account register states.</para>
    /// </summary>
    public async Task<IReadOnlyList<PaymentHistoryDto>> GetOutstandingMonthsAsync(
        Guid stallId, Guid? contractId, DateOnly? forPeriod, CancellationToken ct)
    {
        var stall = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            // Every term, not only the live one: an ended occupancy's arrears are still collectable, and its
            // window can only be worked out in the context of the terms around it.
            .Include(s => s.Contracts)
            .FirstOrDefaultAsync(s => s.Id == stallId, ct);
        if (stall is null)
            return Array.Empty<PaymentHistoryDto>();

        var today = _clock.PhilippineToday;

        var occupancy = ResolveOccupancy(stall, contractId, forPeriod, today);

        if (occupancy is null)
            return Array.Empty<PaymentHistoryDto>();
        var contract = occupancy.Contract;

        // The window this lessee answers for: charges stop at their term's end even if they stayed on, and at the
        // day before the next lessee started.
        var windowStart = occupancy.Start;
        var windowEnd = occupancy.BillableEnd < today ? occupancy.BillableEnd : today;
        if (windowEnd < windowStart)
            return Array.Empty<PaymentHistoryDto>();

        var startMonth = new DateOnly(windowStart.Year, windowStart.Month, 1);
        var endMonth = new DateOnly(windowEnd.Year, windowEnd.Month, 1);

        var rangeStart = windowStart;
        var rangeEnd = windowEnd;
        var rateSnapshot = await _feeRateResolver.GetSnapshotAsync(ct);
        var result = new List<PaymentHistoryDto>();

        if (stall.Facility?.Code == FacilityCode.NPM)
        {
            var dailies = await _context.DailyCollections.AsNoTracking()
                .Where(dc => dc.StallId == stallId && dc.IsPaid && dc.CollectionDate >= rangeStart && dc.CollectionDate <= rangeEnd)
                .Select(dc => new { dc.CollectionDate, dc.DailyFee })
                .ToListAsync(ct);
            var excused = (await _context.DailyCollections.AsNoTracking()
                .Where(dc => dc.StallId == stallId && dc.IsAbsent && dc.CollectionDate >= rangeStart && dc.CollectionDate <= rangeEnd)
                .Select(dc => dc.CollectionDate).ToListAsync(ct)).ToHashSet();
            var closed = await _context.NpmMarketClosures.AsNoTracking()
                .Where(c => c.ClosureDate >= rangeStart && c.ClosureDate <= rangeEnd)
                .Select(c => c.ClosureDate).ToListAsync(ct);
            excused.UnionWith(closed);

            for (var m = startMonth; m <= endMonth; m = m.AddMonths(1))
            {
                var mEndFull = new DateOnly(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month));
                // Clamped to the occupancy at both ends, so a handover month charges each lessee only their days.
                var from = m < windowStart ? windowStart : m;
                var to = mEndFull < windowEnd ? mEndFull : windowEnd;
                // Never past today: this list is what a clerk can take money for, so offering the whole of an
                // in-progress month would collect for days the vendor has not yet occupied. Shared rule.
                to = DomainRules.EarnedThrough(to, _clock.PhilippineToday);
                if (to < from) continue;

                // Every day of the window is chargeable to this lessee by construction — the window already stops
                // at their term's end, at the next lessee's start and at any closure — so the days need counting,
                // not re-testing against the stall's CURRENT contract (which is how an ended occupancy used to
                // come back as owing nothing).
                var daysHeld = to.DayNumber - from.DayNumber + 1;
                var excusedDays = excused.Count(d => d >= from && d <= to);
                if (daysHeld - excusedDays <= 0) continue;
                var fee = stall.ResolveDailyFee(rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, mEndFull));
                var obligation = DomainRules.DailyBilledMonthObligation(
                    fee,
                    stall.ResolveMonthlyRent(
                        rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, mEndFull),
                        rateSnapshot.Resolve(FeeRateKey.NpmMonthlyStall, mEndFull)),
                    mEndFull.Day,
                    daysHeld);
                var bill = obligation - DomainRules.DailyBilledMonthCredit(fee, obligation, daysHeld, excusedDays);
                if (bill <= 0m) continue;
                var paid = dailies.Where(d => d.CollectionDate >= from && d.CollectionDate <= to).Sum(d => d.DailyFee);
                var balance = Math.Max(0m, bill - paid);
                if (balance <= 0m) continue;
                result.Add(new PaymentHistoryDto(
                    $"{m.Year:0000}-{m.Month:00}",
                    paid > 0m ? PaymentStatus.Partial : PaymentStatus.Unpaid,
                    bill, paid, balance, null, null));
            }
        }
        else
        {
            var payments = await _context.PaymentRecords.AsNoTracking()
                .Where(p => p.StallId == stallId)
                .ToListAsync(ct);
            var excusedMonths = (await _context.StallMonthlyExceptions.AsNoTracking()
                .Where(e => e.StallId == stallId)
                .Select(e => new { e.BillingYear, e.BillingMonth }).ToListAsync(ct))
                .Select(e => (e.BillingYear, e.BillingMonth)).ToHashSet();

            // The rent this lessee agreed to. The stall's own rate may since have been set for somebody else, so
            // billing a past occupancy from it would restate history at today's figure.
            var monthlyRate = contract.MonthlyRentalRate > 0 ? contract.MonthlyRentalRate : stall.MonthlyRate;

            for (var m = startMonth; m <= endMonth; m = m.AddMonths(1))
            {
                if (excusedMonths.Contains((m.Year, m.Month))) continue;
                var rec = payments.FirstOrDefault(p => p.BillingYear == m.Year && p.BillingMonth == m.Month);
                if (rec is not null && rec.Status == PaymentStatus.Paid) continue;
                var bill = rec?.TotalBill ?? monthlyRate;
                var paid = rec is not null && rec.Status == PaymentStatus.Partial ? rec.PartialAmount : 0m;
                var balance = Math.Max(0m, bill - paid);
                if (balance <= 0m) continue;
                result.Add(new PaymentHistoryDto(
                    $"{m.Year:0000}-{m.Month:00}",
                    paid > 0m ? PaymentStatus.Partial : PaymentStatus.Unpaid,
                    bill, paid, balance, rec?.ORNumber, null));
            }
        }

        return result;
    }

    /// <summary>
    /// Which lessee a collection screen is talking about: the term it names, the term that held the stall during the
    /// period it is showing, or — when it says neither — the most recent term.
    /// </summary>
    private static StallOccupancy? ResolveOccupancy(Stall stall, Guid? contractId, DateOnly? forPeriod, DateOnly today)    {
        if (contractId is { } id && id != Guid.Empty)
            return stall.ResolveOccupancy(id, today);

        if (forPeriod is { } period)
        {
            var periodEnd = new DateOnly(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month));
            var answerable = stall
                .OccupanciesOverlapping(new DateOnly(period.Year, period.Month, 1), periodEnd, today)
                .OrderByDescending(o => o.Start)
                .FirstOrDefault();

            // A period nobody held the stall in falls back to the current reading rather than reporting nothing.
            if (answerable is not null)
                return answerable;
        }

        return stall.ResolveOccupancy(null, today);
    }

    public async Task<CursorPagedResult<StallCollectionHistoryRowDto>> GetStallCollectionHistoryAsync(
        Guid stallId, DateTime? cursor, int pageSize, CancellationToken ct)
    {
        if (pageSize <= 0) pageSize = 10;

        var stall = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            // Every term: a stall's history spans its lessees, and each row must be named after whoever was
            // answerable on that row's own date — not after whoever holds the stall today.
            .Include(s => s.Contracts)
            .FirstOrDefaultAsync(s => s.Id == stallId, ct);
        if (stall is null)
            return new CursorPagedResult<StallCollectionHistoryRowDto>();

        var occupants = OccupantDirectory.From(new[] { stall }, _clock.PhilippineToday);
        var payorName = stall.ResolveOccupancy(null, _clock.PhilippineToday)?.Contract.ActualOccupant ?? "—";

        if (stall.Facility?.Code == FacilityCode.NPM)
        {
            // Resolve the municipality's NPM fish rate (constant fallback → Cantilan unchanged).
            var npmFish = (await _feeRateResolver.GetSnapshotAsync(ct))
                .Resolve(FeeRateKey.NpmFishPerKilo, _clock.PhilippineToday);
            // NPM: one row per recorded daily collection (paid or absent), newest first; cursor = date.
            var q = _context.DailyCollections.AsNoTracking()
                .Where(d => d.StallId == stallId && (d.IsPaid || d.IsAbsent));
            if (cursor.HasValue)
            {
                var cursorDate = DateOnly.FromDateTime(cursor.Value);
                q = q.Where(d => d.CollectionDate < cursorDate);
            }
            q = q.OrderByDescending(d => d.CollectionDate);

            var paged = await q.ToCursorPagedResultAsync(pageSize, d => d.CollectionDate.ToDateTime(TimeOnly.MinValue), ct);
            var names = await LoadCollectorNamesAsync(paged.Items.Select(d => d.CollectorId), ct);
            // Admin/Head-recorded daily collections carry no CollectorId — resolve the recorder from the
            // audit actor (UpdatedBy ?? CreatedBy) so they attribute the admin instead of showing "—".
            var dAdminNames = await LoadAdminNamesAsync(paged.Items.Select(d => d.UpdatedBy ?? d.CreatedBy), ct);

            return new CursorPagedResult<StallCollectionHistoryRowDto>
            {
                Items = paged.Items.Select(d => new StallCollectionHistoryRowDto(
                    d.CollectionDate.ToDateTime(TimeOnly.MinValue),
                    // The lessee answerable for that business day — a day inside a former occupancy belongs to them.
                    occupants.OnDate(stallId, d.CollectionDate) ?? payorName,
                    d.IsPaid ? "Paid" : "Absent",
                    d.IsPaid ? d.DailyFee + (d.FishKilos.HasValue ? d.FishKilos.Value * npmFish : 0m) : 0m,
                    d.ORNumber,
                    d.CollectorId is Guid cid && names.TryGetValue(cid, out var nm) ? nm : null,
                    // Recorder: the field collector when set, else the admin/Head resolved from the actor.
                    RecordedByName: ResolveRecorderName(d.CollectorId, d.UpdatedBy ?? d.CreatedBy, names, dAdminNames))).ToList(),
                NextCursor = paged.NextCursor,
                HasMore = paged.HasMore
            };
        }

        // Monthly facilities: one row per payment record, newest billing month first; cursor = month.
        var mq = _context.PaymentRecords.AsNoTracking().Where(p => p.StallId == stallId);
        if (cursor.HasValue)
        {
            int cy = cursor.Value.Year, cm = cursor.Value.Month;
            mq = mq.Where(p => p.BillingYear < cy || (p.BillingYear == cy && p.BillingMonth < cm));
        }
        mq = mq.OrderByDescending(p => p.BillingYear).ThenByDescending(p => p.BillingMonth);

        var mPaged = await mq.ToCursorPagedResultAsync(pageSize, p => (DateTime?)new DateTime(p.BillingYear, p.BillingMonth, 1), ct);
        var mNames = await LoadCollectorNamesAsync(mPaged.Items.Select(p => p.CollectorId), ct);
        // Admin/Head-recorded monthly payments carry no CollectorId — resolve the recorder from the
        // audit actor (UpdatedBy ?? CreatedBy) so the history attributes them instead of showing "—".
        var mAdminNames = await LoadAdminNamesAsync(mPaged.Items.Select(p => p.UpdatedBy ?? p.CreatedBy), ct);

        return new CursorPagedResult<StallCollectionHistoryRowDto>
        {
            Items = mPaged.Items.Select(p => new StallCollectionHistoryRowDto(
                new DateTime(p.BillingYear, p.BillingMonth, 1),
                // Attributed by the billing month, so an arrear settled after a handover stays with the lessee who
                // incurred it instead of appearing under the new occupant's name.
                occupants.InMonth(stallId, p.BillingYear, p.BillingMonth) ?? payorName,
                p.Status.ToString(),
                p.AmountPaid,
                p.ORNumber,
                p.CollectorId is Guid cid && mNames.TryGetValue(cid, out var nm) ? nm : null,
                RecordedByName: ResolveRecorderName(p.CollectorId, p.UpdatedBy ?? p.CreatedBy, mNames, mAdminNames))).ToList(),
            NextCursor = mPaged.NextCursor,
            HasMore = mPaged.HasMore
        };
    }

    // Resolves collector display names for a page of records (admin-recorded entries have no collector).
    private async Task<Dictionary<Guid, string>> LoadCollectorNamesAsync(IEnumerable<Guid?> collectorIds, CancellationToken ct)
    {
        var ids = collectorIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();
        return await _context.CollectorUsers
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.FullName ?? string.Empty, ct);
    }

    // Resolves admin/Head display names from audit actors (username → full name). Admin-recorded
    // entries carry no CollectorId; the actor is captured in the audit CreatedBy/UpdatedBy. Mirrors
    // DashboardRepository's adminNames mapping. Built once per page (no N+1).
    private async Task<Dictionary<string, string>> LoadAdminNamesAsync(IEnumerable<string?> actors, CancellationToken ct)
    {
        var keys = actors.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!).Distinct().ToList();
        if (keys.Count == 0)
            return new Dictionary<string, string>();
        return await _context.AdminUsers
            .AsNoTracking()
            .Where(a => a.Username != null && keys.Contains(a.Username))
            .ToDictionaryAsync(a => a.Username!, a => a.FullName ?? a.Username!, ct);
    }

    // Display-only recorder attribution (mirrors DashboardRepository.ResolveRecorder): the field
    // collector when a CollectorId is set, otherwise the admin/Head resolved from the audit actor,
    // falling back to the raw actor. Returns null when nothing is known (UI renders it as "—").
    private static string? ResolveRecorderName(
        Guid? collectorId,
        string? actor,
        IReadOnlyDictionary<Guid, string> collectorNames,
        IReadOnlyDictionary<string, string> adminNames)
    {
        if (collectorId is { } id
            && collectorNames.TryGetValue(id, out var collector)
            && !string.IsNullOrWhiteSpace(collector))
            return collector;

        if (!string.IsNullOrWhiteSpace(actor))
            return adminNames.TryGetValue(actor, out var admin) ? admin : actor;

        return null;
    }

    // Days in [start, end] where the stall is active and under an effective contract (NPM-style).
    private static int CountCollectableDays(Stall stall, DateOnly start, DateOnly end)
    {
        if (end < start || stall.Status != StallStatus.Active) return 0;
        var days = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (stall.Contracts.Any(c => c.IsActive && c.EffectivityDate <= d && d <= c.ExpiryDate))
                days++;
        }
        return days;
    }
}
