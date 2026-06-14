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

    public async Task<IReadOnlyList<NpmStallDailyStatusDto>> GetNpmDailyStatusAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Paid daily collections for the facility in the selected month. NPM operational status is
        // derived from daily collections (not monthly payment records) because the market is
        // collected day-by-day at ₱30/day.
        var collections = await context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.Stall!.Facility!.Code == facilityCode
                && dc.IsPaid
                && dc.CollectionDate >= monthStart
                && dc.CollectionDate <= monthEnd)
            .Select(dc => new { dc.StallId, dc.CollectionDate })
            .ToListAsync(ct);

        return collections
            .GroupBy(c => c.StallId)
            .Select(g => new NpmStallDailyStatusDto(
                g.Key,
                g.Any(x => x.CollectionDate == today),
                g.Select(x => x.CollectionDate).Distinct().Count(),
                g.Max(x => x.CollectionDate)))
            .ToList();
    }

    public async Task<IReadOnlyList<PaymentHistoryDto>> GetPaymentHistoryAsync(Guid stallId, CancellationToken ct)
    {
        var now = PhilippineTime.Now;
        var today = PhilippineTime.Today;
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
        var windowStart = new DateOnly(startDate.Year, startDate.Month, 1);
        var dailies = await context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.StallId == stallId && dc.IsPaid
                && dc.CollectionDate >= windowStart && dc.CollectionDate <= today)
            .Select(dc => new { dc.CollectionDate, dc.DailyFee, dc.CollectorId, dc.ORNumber })
            .ToListAsync(ct);

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

            // A recorded monthly payment (admin-entered) takes precedence over daily aggregation.
            var rec = payments.FirstOrDefault(p => p.BillingYear == year && p.BillingMonth == month);
            if (rec is not null && rec.Status != PaymentStatus.Unpaid)
            {
                result.Add(new PaymentHistoryDto(period, rec.Status, rec.TotalBill, rec.AmountPaid, rec.BalanceDue,
                    rec.ORNumber, rec.PaidAt,
                    rec.CollectorId is Guid rcid && collectorNames.TryGetValue(rcid, out var rn) ? rn : null));
                continue;
            }

            // Obligation = collectable days that month × ₱30 (contract-aware, same basis as reports).
            var collectableDays = CountCollectableDays(stall, monthStart, monthEnd);
            var bill = collectableDays * FeeRates.NpmDailyFee;
            var monthDailies = dailies.Where(d => d.CollectionDate >= monthStart && d.CollectionDate <= monthEnd).ToList();
            var amountPaid = monthDailies.Sum(d => d.DailyFee);

            // Only emit a row for months with actual daily collections. Months with no payment are
            // intentionally omitted so the modal can render them correctly: pre-contract months are
            // greyed out as "N/A", and collectable-but-unpaid months show as Unpaid. Emitting a
            // zero row here would defeat the modal's before-contract detection.
            if (amountPaid <= 0m)
                continue;

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
    /// under an effective contract: a non-Unpaid monthly record takes precedence; otherwise NPM folds
    /// that month's paid daily collections against the contract-aware ₱30/day obligation, while other
    /// facilities owe the full monthly rent. Mirrors <see cref="GetPaymentHistoryAsync"/> so the
    /// profile summary reconciles with the history grid and the reports.
    /// </summary>
    public async Task<StallLedgerSummaryDto> GetStallLedgerSummaryAsync(Guid stallId, CancellationToken ct)
    {
        var now = PhilippineTime.Now;
        var today = PhilippineTime.Today;
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
        var dailies = isNpm
            ? await context.DailyCollections
                .AsNoTracking()
                .Where(dc => dc.StallId == stallId && dc.IsPaid
                    && dc.CollectionDate >= windowStart && dc.CollectionDate <= today)
                .Select(dc => new { dc.CollectionDate, dc.DailyFee })
                .ToListAsync(ct)
            : new();

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

            // A recorded (non-Unpaid) monthly payment is authoritative for both NPM and others.
            if (rec is not null && rec.Status != PaymentStatus.Unpaid)
            {
                totalCollected += rec.AmountPaid;
                totalOutstanding += rec.BalanceDue;
                if (rec.Status == PaymentStatus.Paid) monthsPaid++; else monthsUnpaid++;
                continue;
            }

            if (isNpm)
            {
                var bill = CountCollectableDays(stall, monthStart, monthEnd) * FeeRates.NpmDailyFee;
                var paid = dailies.Where(d => d.CollectionDate >= monthStart && d.CollectionDate <= monthEnd).Sum(d => d.DailyFee);
                totalCollected += paid;
                totalOutstanding += Math.Max(0m, bill - paid);
                if (bill > 0m && paid >= bill) monthsPaid++; else monthsUnpaid++;
            }
            else
            {
                // Monthly-billed facility with no record this month → full rent owed.
                totalOutstanding += stall.MonthlyRate;
                monthsUnpaid++;
            }
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
