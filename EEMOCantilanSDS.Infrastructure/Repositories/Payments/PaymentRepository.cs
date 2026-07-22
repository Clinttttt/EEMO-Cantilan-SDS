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

public class PaymentRepository(AppDbContext context, IFeeRateResolver feeRateResolver) : IPaymentRepository
{
    // Test/non-DI convenience: resolves fees from the context (empty rate table => ordinance constants).
    public PaymentRepository(AppDbContext context) : this(context, new FeeRateResolver(context)) { }

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

        // Resolve the municipality's NPM fish rate as of the period (constant fallback → Cantilan unchanged).
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var npmFish = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, monthStart);

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
                p.StallId,
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
                + (p.FishKilos.HasValue ? p.FishKilos.Value * npmFish : 0m),
            1,
            IsDaily: false,
            StallId: p.StallId,
            Year: year,
            Month: month));

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
                dc.StallId,
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
                // Daily fee only — the ₱1/kg fish surcharge is tracked separately (as in the NPM/Export
                // reports), so it is excluded from this "Daily receipt · OR" amount.
                g.Sum(x => x.DailyFee),
                g.Count(),
                IsDaily: true,
                StallId: g.First().StallId,
                Year: year,
                Month: month));

        return monthly.Concat(daily).ToList();
    }

    /// <summary>
    /// Whole-year variant of <see cref="GetUnreceiptedCashPaymentsAsync"/>: every fully-paid cash/field
    /// record for the year that still lacks an OR, one row per (stall, billing month). Used by the
    /// Follow-up History "Whole year" view so a blank-OR settlement in ANY month surfaces under Missing OR
    /// (the single-month path is unchanged). Online payments are excluded (they have their own queue).
    /// </summary>
    public async Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsForYearAsync(int year, CancellationToken ct)
    {
        var yearStart = new DateOnly(year, 1, 1);
        var yearEnd = new DateOnly(year, 12, 31);

        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var npmFish = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, yearStart);

        var onlineRecordIds = context.OnlinePaymentTransactions.Select(t => t.PaymentRecordId);

        // ── Monthly: fully-Paid records with a blank OR, any billing month of the year ──
        var monthlyRaw = await context.PaymentRecords
            .AsNoTracking()
            .Where(p => p.BillingYear == year
                && p.Status == PaymentStatus.Paid
                && (p.ORNumber == null || p.ORNumber == "")
                && !onlineRecordIds.Contains(p.Id))
            .Select(p => new
            {
                Code = p.Stall!.Facility!.Code,
                p.Stall.StallNo,
                p.StallId,
                Occupant = p.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                p.BillingMonth,
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
                + (p.FishKilos.HasValue ? p.FishKilos.Value * npmFish : 0m),
            1,
            IsDaily: false,
            StallId: p.StallId,
            Year: year,
            Month: p.BillingMonth));

        // ── NPM daily: paid blank-OR days, grouped per (stall, calendar month) of the year ──
        var dailyRaw = await context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.IsPaid
                && (dc.ORNumber == null || dc.ORNumber == "")
                && dc.CollectionDate >= yearStart && dc.CollectionDate <= yearEnd)
            .Select(dc => new
            {
                Code = dc.Stall!.Facility!.Code,
                dc.Stall.StallNo,
                dc.StallId,
                Occupant = dc.Stall.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault(),
                Month = dc.CollectionDate.Month,
                dc.DailyFee,
                dc.FishKilos
            })
            .ToListAsync(ct);

        var daily = dailyRaw
            .GroupBy(d => new { d.Code, d.StallNo, d.Occupant, d.Month })
            .Select(g => new UnreceiptedPaymentDto(
                g.Key.Code,
                g.Key.StallNo,
                string.IsNullOrWhiteSpace(g.Key.Occupant) ? string.Empty : g.Key.Occupant!,
                g.Sum(x => x.DailyFee),
                g.Count(),
                IsDaily: true,
                StallId: g.First().StallId,
                Year: year,
                Month: g.Key.Month));

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

        // Current-month electricity+water bill status per stall. Status is a computed (derived) property,
        // so bills are materialised first and the status computed in memory. One bill per stall/month.
        var utilityByStall = (await context.UtilityBills
                .AsNoTracking()
                .Where(u => u.Stall!.Facility!.Code == facilityCode
                    && u.BillingYear == year && u.BillingMonth == month)
                .ToListAsync(ct))
            .GroupBy(u => u.StallId)
            .ToDictionary(g => g.Key, g => g.First().Status);

        // Build a row for every stall with EITHER daily-collection activity OR a current-month utility
        // bill. A stall that only has an (unpaid) utility bill and no daily collections yet must still
        // surface its utility status, so the operational card's utility icon can colour it (red = unpaid).
        var collectionsByStall = collections.GroupBy(c => c.StallId).ToDictionary(g => g.Key, g => g.ToList());
        var stallIds = collectionsByStall.Keys.Union(utilityByStall.Keys);

        return stallIds
            .Select(stallId =>
            {
                var rows = collectionsByStall.TryGetValue(stallId, out var r) ? r : new();
                var paid = rows.Where(x => x.IsPaid).ToList();
                return new NpmStallDailyStatusDto(
                    stallId,
                    paid.Any(x => x.CollectionDate == today),
                    paid.Select(x => x.CollectionDate).Distinct().Count(),
                    paid.Count > 0 ? paid.Max(x => x.CollectionDate) : (DateOnly?)null,
                    // Most recent paid day's OR (skipping blanks) — the receipt's reference OR.
                    paid.OrderByDescending(x => x.CollectionDate)
                        .Select(x => x.ORNumber)
                        .FirstOrDefault(or => !string.IsNullOrWhiteSpace(or)),
                    rows.Any(x => x.IsAbsent && x.CollectionDate == today),
                    // OR of the single most-recent paid day (may be blank → that day is awaiting an OR).
                    paid.OrderByDescending(x => x.CollectionDate).FirstOrDefault()?.ORNumber,
                    // Current-month utility (elec+water) bill status (null when no bill this month).
                    utilityByStall.TryGetValue(stallId, out var us) ? us : (PaymentStatus?)null);
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
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
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
            var bill = billableDays * stall.ResolveDailyFee(rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, monthEnd));
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
                        period, PaymentStatus.Paid, 0m, 0m, 0m, null, null, null, IsExcused: true));                }
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

        // Resolve the municipality's NPM rates (constant fallback → Cantilan unchanged); each month's
        // ₱/day obligation is resolved as of that month below.
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);

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
                var bill = billableDays * stall.ResolveDailyFee(rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, monthEnd));
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

    public async Task<IReadOnlyList<PaymentHistoryDto>> GetOutstandingMonthsAsync(Guid stallId, CancellationToken ct)
    {
        var stall = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .FirstOrDefaultAsync(s => s.Id == stallId, ct);
        if (stall is null)
            return Array.Empty<PaymentHistoryDto>();

        var contract = stall.Contracts.FirstOrDefault(c => c.IsActive);
        if (contract is null)
            return Array.Empty<PaymentHistoryDto>();

        var today = PhilippineTime.Today;
        var startMonth = new DateOnly(contract.EffectivityDate.Year, contract.EffectivityDate.Month, 1);
        // Bill up to the earlier of today or the contract's expiry — never future days.
        var lastRef = contract.ExpiryDate < today ? contract.ExpiryDate : today;
        var endMonth = new DateOnly(lastRef.Year, lastRef.Month, 1);
        if (endMonth < startMonth)
            return Array.Empty<PaymentHistoryDto>();

        var rangeStart = startMonth;
        var rangeEnd = new DateOnly(endMonth.Year, endMonth.Month, DateTime.DaysInMonth(endMonth.Year, endMonth.Month));
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var result = new List<PaymentHistoryDto>();

        if (stall.Facility?.Code == FacilityCode.NPM)
        {
            var dailies = await context.DailyCollections.AsNoTracking()
                .Where(dc => dc.StallId == stallId && dc.IsPaid && dc.CollectionDate >= rangeStart && dc.CollectionDate <= rangeEnd)
                .Select(dc => new { dc.CollectionDate, dc.DailyFee })
                .ToListAsync(ct);
            var excused = (await context.DailyCollections.AsNoTracking()
                .Where(dc => dc.StallId == stallId && dc.IsAbsent && dc.CollectionDate >= rangeStart && dc.CollectionDate <= rangeEnd)
                .Select(dc => dc.CollectionDate).ToListAsync(ct)).ToHashSet();
            var closed = await context.NpmMarketClosures.AsNoTracking()
                .Where(c => c.ClosureDate >= rangeStart && c.ClosureDate <= rangeEnd)
                .Select(c => c.ClosureDate).ToListAsync(ct);
            excused.UnionWith(closed);

            for (var m = startMonth; m <= endMonth; m = m.AddMonths(1))
            {
                var mEndFull = new DateOnly(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month));
                var mEnd = mEndFull < today ? mEndFull : today;      // never count future days
                var collectableDays = CountCollectableDays(stall, m, mEnd);
                var excusedDays = excused.Count(d => d >= m && d <= mEnd);
                var billableDays = Math.Max(0, collectableDays - excusedDays);
                if (billableDays == 0) continue;
                var bill = billableDays * stall.ResolveDailyFee(rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, mEndFull));
                var paid = dailies.Where(d => d.CollectionDate >= m && d.CollectionDate <= mEndFull).Sum(d => d.DailyFee);
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
            var payments = await context.PaymentRecords.AsNoTracking()
                .Where(p => p.StallId == stallId)
                .ToListAsync(ct);
            var excusedMonths = (await context.StallMonthlyExceptions.AsNoTracking()
                .Where(e => e.StallId == stallId)
                .Select(e => new { e.BillingYear, e.BillingMonth }).ToListAsync(ct))
                .Select(e => (e.BillingYear, e.BillingMonth)).ToHashSet();

            for (var m = startMonth; m <= endMonth; m = m.AddMonths(1))
            {
                if (excusedMonths.Contains((m.Year, m.Month))) continue;
                var rec = payments.FirstOrDefault(p => p.BillingYear == m.Year && p.BillingMonth == m.Month);
                if (rec is not null && rec.Status == PaymentStatus.Paid) continue;
                var bill = rec?.TotalBill ?? stall.MonthlyRate;
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

    public async Task<CursorPagedResult<StallCollectionHistoryRowDto>> GetStallCollectionHistoryAsync(
        Guid stallId, DateTime? cursor, int pageSize, CancellationToken ct)
    {
        if (pageSize <= 0) pageSize = 10;

        var stall = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .FirstOrDefaultAsync(s => s.Id == stallId, ct);
        if (stall is null)
            return new CursorPagedResult<StallCollectionHistoryRowDto>();

        var payorName = stall.Contracts.FirstOrDefault(c => c.IsActive)?.ActualOccupant ?? "—";

        if (stall.Facility?.Code == FacilityCode.NPM)
        {
            // Resolve the municipality's NPM fish rate (constant fallback → Cantilan unchanged).
            var npmFish = (await feeRateResolver.GetSnapshotAsync(ct))
                .Resolve(FeeRateKey.NpmFishPerKilo, PhilippineTime.Today);
            // NPM: one row per recorded daily collection (paid or absent), newest first; cursor = date.
            var q = context.DailyCollections.AsNoTracking()
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
                    payorName,
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
        var mq = context.PaymentRecords.AsNoTracking().Where(p => p.StallId == stallId);
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
                payorName,
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
        return await context.CollectorUsers
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
        return await context.AdminUsers
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

    public async Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct)
    {
        // OR (receipt) numbers must stay unique even against soft-deleted records, so bypass the global
        // IsDeleted filter. Scope to the caller's municipality when it is resolved, so a second LGU may
        // reuse an OR number that only exists in another LGU. Token-less/setup flows have an empty tenant
        // (mid == Guid.Empty) and keep the original global check — for Cantilan (the only tenant with data)
        // the scoped and global results are identical. Delegated to the shared registry so the module list
        // (payments, daily, slaughter, TPM, TRM, utilities) can never drift between callers.
        return await OrNumberRegistry.IsAvailableAsync(context, orNumber, ct);
    }

    public async Task<bool> IsDailyCollectionOrAvailableForStallAsync(string orNumber, Guid stallId, CancellationToken ct)
    {
        // Same rules as IsORNumberUniqueAsync, but one OR may recur across multiple days of THIS stall
        // (one receipt covering several days). Still rejected if the OR is on a different stall/module.
        return await OrNumberRegistry.IsAvailableAsync(context, orNumber, ct, allowDailyStall: stallId);
    }

    public async Task<bool> IsMonthlyOrAvailableForStallAsync(string orNumber, Guid stallId, CancellationToken ct)
    {
        // Same rules as IsORNumberUniqueAsync, but one OR may settle multiple months of THIS stall
        // (one receipt for "all outstanding"). Still rejected if the OR is on a different stall/module.
        return await OrNumberRegistry.IsAvailableAsync(context, orNumber, ct, allowMonthlyStall: stallId);
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

