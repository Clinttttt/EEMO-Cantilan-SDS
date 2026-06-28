using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class PaymentRepository(AppDbContext context) : IPaymentRepository
{
    public async Task<PaymentRecord?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await context.PaymentRecords.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<PaymentRecordDto?> GetPaymentRecordAsync(Guid stallId, int year, int month, CancellationToken ct)
    {
        var payment = await context.PaymentRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.StallId == stallId && p.BillingYear == year && p.BillingMonth == month, ct);

        if (payment == null)
            return null;

        return new PaymentRecordDto(
            payment.Id,
            payment.Status,
            payment.ORNumber,
            payment.BaseRentalAmount,
            payment.ElecAmount,
            payment.WaterAmount,
            payment.FishFeeAmount,
            payment.AmountPaid,
            payment.BalanceDue
        );
    }

    public async Task<IReadOnlyList<FacilityPaymentRecordDto>> GetFacilityPaymentRecordsAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var payments = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.Stall!.Facility!.Code == facilityCode && p.BillingYear == year && p.BillingMonth == month)
            .ToListAsync(ct);

        // AmountPaid is a C# computed property — map in memory, not in SQL
        return payments
            .Select(p => new FacilityPaymentRecordDto(p.StallId, p.Status, p.ORNumber, p.AmountPaid))
            .ToList();
    }

    public async Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsAsync(int year, int month, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Online payments also leave ORNumber null until staff encode it, but they have their own
        // awaiting-OR queue (keyed by an OnlinePaymentTransaction). Exclude them here so a payment is
        // never listed under both "online awaiting OR" and "cash awaiting OR".
        var onlineRecordIds = context.OnlinePaymentTransactions.Select(t => t.PaymentRecordId);

        // ── Monthly: fully-Paid records with a blank OR (cash/field), for the selected billing period ──
        // Restricted to Paid (not Partial) so this never overlaps the current-period "Partial" follow-up.
        var monthlyRaw = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.BillingYear == year && p.BillingMonth == month
                && p.Status == PaymentStatus.Paid
                && (p.ORNumber == null || p.ORNumber == "")
                && !onlineRecordIds.Contains(p.Id))
            .Select(p => new
            {
                Code = p.Stall!.Facility!.Code,
                p.Stall.StallNo,
                Occupant = p.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                p.BaseRentalAmount,
                p.ElecAmount,
                p.WaterAmount,
                p.FishKilos
            })
            .ToListAsync(ct);

        var monthly = monthlyRaw.Select(p => new UnreceiptedPaymentDto(
            p.Code,
            p.StallNo,
            string.IsNullOrWhiteSpace(p.Occupant) ? string.Empty : p.Occupant!,
            p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0)
                + (p.FishKilos.HasValue ? p.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m),
            1,
            IsDaily: false));

        // ── NPM daily: paid daily collections with a blank OR, grouped per stall for the period ──
        var dailyRaw = await context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.IsPaid
                && (dc.ORNumber == null || dc.ORNumber == "")
                && dc.CollectionDate >= monthStart && dc.CollectionDate <= monthEnd)
            .Select(dc => new
            {
                Code = dc.Stall!.Facility!.Code,
                dc.Stall.StallNo,
                Occupant = dc.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                dc.DailyFee,
                dc.FishKilos
            })
            .ToListAsync(ct);

        var daily = dailyRaw
            .GroupBy(d => new { d.Code, d.StallNo, d.Occupant })
            .Select(g => new UnreceiptedPaymentDto(
                g.Key.Code,
                g.Key.StallNo,
                string.IsNullOrWhiteSpace(g.Key.Occupant) ? string.Empty : g.Key.Occupant!,
                g.Sum(x => x.DailyFee + (x.FishKilos.HasValue ? x.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m)),
                g.Count(),
                IsDaily: true));

        return monthly.Concat(daily).ToList();
    }

    public async Task<IReadOnlyList<NpmStallDailyStatusDto>> GetNpmDailyStatusAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Daily collections for the facility this month — paid days drive the operational stats, and
        // today's absent marker drives the "Absent" pill. NPM status is daily (not monthly records).
        var collections = await context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.Stall!.Facility!.Code == facilityCode
                && (dc.IsPaid || dc.IsAbsent)
                && dc.CollectionDate >= monthStart
                && dc.CollectionDate <= monthEnd)
            .Select(dc => new { dc.StallId, dc.CollectionDate, dc.ORNumber, dc.IsPaid, dc.IsAbsent })
            .ToListAsync(ct);

        return collections
            .GroupBy(c => c.StallId)
            .Select(g =>
            {
                var paid = g.Where(x => x.IsPaid).ToList();
                return new NpmStallDailyStatusDto(
                    g.Key,
                    paid.Any(x => x.CollectionDate == today),
                    paid.Select(x => x.CollectionDate).Distinct().Count(),
                    paid.Count > 0 ? paid.Max(x => x.CollectionDate) : (DateOnly?)null,
                    // Most recent paid day's OR (skipping blanks) — the receipt's reference OR.
                    paid.OrderByDescending(x => x.CollectionDate)
                        .Select(x => x.ORNumber)
                        .FirstOrDefault(or => !string.IsNullOrWhiteSpace(or)),
                    g.Any(x => x.IsAbsent && x.CollectionDate == today),
                    // OR of the single most-recent paid day (may be blank → that day is awaiting an OR).
                    paid.OrderByDescending(x => x.CollectionDate).FirstOrDefault()?.ORNumber);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<PaymentHistoryDto>> GetPaymentHistoryAsync(Guid stallId, CancellationToken ct)
    {
        var now = PhilippineTime.Now;
        var startDate = now.AddMonths(-11);

        var stall = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .FirstOrDefaultAsync(s => s.Id == stallId, ct);

        var payments = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.StallId == stallId)
            .Where(p => (p.BillingYear > startDate.Year) || (p.BillingYear == startDate.Year && p.BillingMonth >= startDate.Month))
            .ToListAsync(ct);

        // Non-NPM facilities are billed monthly — the payment record is the source of truth.
        if (stall?.Facility?.Code != FacilityCode.NPM)
        {
            return payments
                .OrderByDescending(p => p.BillingYear)
                .ThenByDescending(p => p.BillingMonth)
                .Select(p => new PaymentHistoryDto(
                    $"{p.BillingYear:0000}-{p.BillingMonth:00}",
                    p.Status, p.TotalBill, p.AmountPaid, p.BalanceDue, p.ORNumber, p.PaidAt, null))
                .ToList();
        }

        // NPM is collected daily — fold each month's daily collections into the monthly ledger so
        // the history reflects reality (a stall paying ₱30/day is not "Unpaid" for the month).
        // Window runs to the end of the CURRENT month (not clamped to today) so days paid in
        // advance still count — this mirrors the daily collection calendar, which shows every
        // paid day of the month regardless of whether the date has arrived yet.
        var windowStart = new DateOnly(startDate.Year, startDate.Month, 1);
        var windowEnd = new DateOnly(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
        var dailies = await context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.StallId == stallId && dc.IsPaid
                && dc.CollectionDate >= windowStart && dc.CollectionDate <= windowEnd)
            .Select(dc => new { dc.CollectionDate, dc.DailyFee, dc.CollectorId, dc.ORNumber })
            .ToListAsync(ct);

        // Excused/absent dates — these days are not owed, so they reduce each month's ₱30/day bill
        // (and a month entirely absent becomes a ₱0 "Absent" row instead of an Unpaid one).
        var absentDates = (await context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.StallId == stallId && dc.IsAbsent
                && dc.CollectionDate >= windowStart && dc.CollectionDate <= windowEnd)
            .Select(dc => dc.CollectionDate)
            .ToListAsync(ct))
            .ToHashSet();

        // NPM market closures (facility-wide) also excuse the day — fold them in so a closed day is
        // never billed/Unpaid in the history (mirrors the ledger summary + Financial Reports).
        var historyClosedDates = await context.NpmMarketClosures
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
            : await context.CollectorUsers
                .Where(c => collectorIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.FullName ?? string.Empty, ct);

        var result = new List<PaymentHistoryDto>();
        for (var i = 11; i >= 0; i--)
        {
            var m = now.AddMonths(-i);
            int year = m.Year, month = m.Month;
            var period = $"{year:0000}-{month:00}";
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

            // NPM money is ALWAYS daily-truth: a flat monthly PaymentRecord (e.g. an admin-entered
            // partial) must never override the day-by-day reality, because NPM is a ₱30/day facility.
            // Obligation = collectable days that month × ₱30 (contract-aware, same basis as reports);
            // collected = the sum of paid daily collections for the month. The monthly record's flat
            // ₱900 base / raw partial is intentionally ignored here.
            var collectableDays = CountCollectableDays(stall, monthStart, monthEnd);
            var absentDays = absentDates.Count(d => d >= monthStart && d <= monthEnd);
            var billableDays = Math.Max(0, collectableDays - absentDays);
            var bill = billableDays * FeeRates.NpmDailyFee;
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
                if (collectableDays > 0 && billableDays == 0 && absentDays > 0)
                {
                    result.Add(new PaymentHistoryDto(
                        period, PaymentStatus.Paid, 0m, 0m, 0m, null, null, null, IsExcused: true));
                }
                continue;
            }

            var status = amountPaid >= bill && bill > 0m ? PaymentStatus.Paid : PaymentStatus.Partial;
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
                last?.CollectorId is Guid lcid && collectorNames.TryGetValue(lcid, out var ln) ? ln : null));
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
        var now = PhilippineTime.Now;
        var startDate = now.AddMonths(-11);

        var stall = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .FirstOrDefaultAsync(s => s.Id == stallId, ct);

        if (stall is null)
            return new StallLedgerSummaryDto(0, 0, 0m, 0m);

        var payments = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.StallId == stallId)
            .Where(p => (p.BillingYear > startDate.Year) || (p.BillingYear == startDate.Year && p.BillingMonth >= startDate.Month))
            .ToListAsync(ct);

        var isNpm = stall.Facility?.Code == FacilityCode.NPM;

        var windowStart = new DateOnly(startDate.Year, startDate.Month, 1);
        var windowEnd = new DateOnly(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month));
        var dailies = isNpm
            ? await context.DailyCollections
                .AsNoTracking()
                .Where(dc => dc.StallId == stallId && dc.IsPaid
                    && dc.CollectionDate >= windowStart && dc.CollectionDate <= windowEnd)
                .Select(dc => new { dc.CollectionDate, dc.DailyFee })
                .ToListAsync(ct)
            : new();

        // Excused/absent dates reduce the NPM obligation (a fully-absent month is not owed at all).
        var absentDates = isNpm
            ? (await context.DailyCollections
                .AsNoTracking()
                .Where(dc => dc.StallId == stallId && dc.IsAbsent
                    && dc.CollectionDate >= windowStart && dc.CollectionDate <= windowEnd)
                .Select(dc => dc.CollectionDate)
                .ToListAsync(ct))
                .ToHashSet()
            : new HashSet<DateOnly>();

        // NPM market closures (facility-wide) also excuse the day — union them with per-stall absences.
        var marketClosedDates = isNpm
            ? (await context.NpmMarketClosures
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
            : (await context.StallMonthlyExceptions
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

            // Skip months the stall was not active / under an effective contract (not yet due).
            if (CountCollectableDays(stall, monthStart, monthEnd) == 0)
                continue;

            var rec = payments.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);

            if (isNpm)
            {
                // NPM money is always daily-truth (₱30/day × contract-prorated days) — a flat monthly
                // record never overrides it. Excused/absent/market-closed days are not owed, so they
                // reduce the bill; a month entirely excused is skipped (not paid, not unpaid).
                var npmExcused = excusedDates.Count(d => d >= monthStart && d <= monthEnd);
                var billableDays = Math.Max(0, CountCollectableDays(stall, monthStart, monthEnd) - npmExcused);
                var bill = billableDays * FeeRates.NpmDailyFee;
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

    public async Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct)
    {
        // OR (receipt) numbers must stay globally unique even against soft-deleted records,
        // so bypass the global IsDeleted filter for these existence checks.
        if (await context.PaymentRecords.IgnoreQueryFilters().AnyAsync(p => p.ORNumber == orNumber, ct)) return false;
        if (await context.DailyCollections.IgnoreQueryFilters().AnyAsync(d => d.ORNumber == orNumber, ct)) return false;
        if (await context.SlaughterTransactions.IgnoreQueryFilters().AnyAsync(s => s.ORNumber == orNumber, ct)) return false;
        if (await context.TpmAttendances.IgnoreQueryFilters().AnyAsync(a => a.ORNumber == orNumber, ct)) return false;
        if (await context.TrmTrips.IgnoreQueryFilters().AnyAsync(t => t.ORNumber == orNumber, ct)) return false;
        return true;
    }

    public async Task AddAsync(PaymentRecord payment, CancellationToken ct)
    {
        await context.PaymentRecords.AddAsync(payment, ct);
    }

    public async Task UpdateAsync(PaymentRecord payment, CancellationToken ct)
    {
        context.PaymentRecords.Update(payment);
        await Task.CompletedTask;
    }
}
