using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class OnlinePaymentRepository(AppDbContext context) : IOnlinePaymentRepository
{
    public async Task<OnlinePaymentTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await context.OnlinePaymentTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<OnlinePaymentTransaction?> GetByGatewayReferenceAsync(string gatewayReference, CancellationToken ct = default)
    {
        // The webhook is anonymous, so the request resolves to the DEFAULT tenant; the gateway reference is
        // globally unique, so bypass the tenant filter here to find a transaction belonging to ANY LGU. The
        // webhook handler then pins the request to that transaction's municipality before settling.
        return await context.OnlinePaymentTransactions
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted)
            .FirstOrDefaultAsync(t => t.GatewayReference == gatewayReference, ct);
    }

    public async Task<OnlinePaymentTransaction?> GetByReferenceAsync(string reference, CancellationToken ct = default)
    {
        return await context.OnlinePaymentTransactions
            .FirstOrDefaultAsync(t => t.Reference == reference, ct);
    }

    public async Task<bool> ReferenceExistsAsync(string reference, CancellationToken ct = default)
    {
        return await context.OnlinePaymentTransactions
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Reference == reference, ct);
    }

    public async Task AddAsync(OnlinePaymentTransaction transaction, CancellationToken ct = default)
    {
        await context.OnlinePaymentTransactions.AddAsync(transaction, ct);
    }

    public async Task<OnlinePaymentTransaction?> GetResumableTransactionForRecordAsync(Guid paymentRecordId, CancellationToken ct = default)
    {
        return await context.OnlinePaymentTransactions
            .Where(t => t.PaymentRecordId == paymentRecordId
                && (t.Status == OnlinePaymentStatus.Initiated || t.Status == OnlinePaymentStatus.Pending))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<OnlinePaymentTransaction?> GetResumableNpmTransactionAsync(Guid stallId, int year, int month, OnlinePaymentTargetKind kind, CancellationToken ct = default)
    {
        return await context.OnlinePaymentTransactions
            .Where(t => t.TargetKind == kind
                && t.TargetStallId == stallId && t.TargetYear == year && t.TargetMonth == month
                && (t.Status == OnlinePaymentStatus.Initiated || t.Status == OnlinePaymentStatus.Pending))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<OnlinePaymentTransaction?> GetResumableNpmFishDayTransactionAsync(Guid stallId, int year, int month, int day, CancellationToken ct = default)
    {
        return await context.OnlinePaymentTransactions
            .Where(t => t.TargetKind == OnlinePaymentTargetKind.NpmFishDay
                && t.TargetStallId == stallId && t.TargetYear == year && t.TargetMonth == month && t.TargetDay == day
                && (t.Status == OnlinePaymentStatus.Initiated || t.Status == OnlinePaymentStatus.Pending))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<OnlinePaymentAwaitingOrDto>> GetAwaitingOrAsync(CancellationToken ct = default)
        => await GetAwaitingOrCoreAsync(null, null, ct);

    public async Task<IReadOnlyList<OnlinePaymentAwaitingOrDto>> GetAwaitingOrByPeriodAsync(int year, int month, CancellationToken ct = default)
        => await GetAwaitingOrCoreAsync(year, month, ct);

    private async Task<IReadOnlyList<OnlinePaymentAwaitingOrDto>> GetAwaitingOrCoreAsync(int? year, int? month, CancellationToken ct)
    {
        // ── Monthly-rental targets: joined to their PaymentRecord (unchanged behaviour). ──
        var monthlyRows = await (
            from t in context.OnlinePaymentTransactions
            where t.Status == OnlinePaymentStatus.Paid && t.TargetKind == OnlinePaymentTargetKind.MonthlyRecord
            join r in context.PaymentRecords on t.PaymentRecordId equals (Guid?)r.Id
            // Skip any whose OR is already on the ledger record (e.g. encoded from the mobile side) so the
            // same payment can't show up here as still needing an OR.
            where r.ORNumber == null
            where year == null || (r.BillingYear == year && r.BillingMonth == month)
            join s in context.Stalls on r.StallId equals s.Id
            join f in context.Facilities on s.FacilityId equals f.Id
            join u in context.PayorUsers on t.PayorUserId equals u.Id into payor
            from u in payor.DefaultIfEmpty()
            select new
            {
                t.Id,
                t.Reference,
                Facility = f.Code,
                s.StallNo,
                PayorName = u != null ? u.FullName : null,
                BillingYear = r.BillingYear,
                BillingMonth = r.BillingMonth,
                t.Amount,
                t.Method,
                t.PaidAt
            }).ToListAsync(ct);

        // ── NPM daily-month targets: no PaymentRecord — derive stall/period from the target, and treat as
        //    still-awaiting only while the settled month has at least one paid day without an OR yet. ──
        var npmRows = await (
            from t in context.OnlinePaymentTransactions
            where t.Status == OnlinePaymentStatus.Paid && t.TargetKind == OnlinePaymentTargetKind.NpmDailyMonth
            where year == null || (t.TargetYear == year && t.TargetMonth == month)
            join s in context.Stalls on t.TargetStallId equals (Guid?)s.Id
            join f in context.Facilities on s.FacilityId equals f.Id
            join u in context.PayorUsers on t.PayorUserId equals u.Id into payor
            from u in payor.DefaultIfEmpty()
            where context.DailyCollections.Any(d => d.StallId == t.TargetStallId
                && d.CollectionDate.Year == t.TargetYear
                && d.CollectionDate.Month == t.TargetMonth
                && d.IsPaid
                && (d.ORNumber == null || d.ORNumber == ""))
            select new
            {
                t.Id,
                t.Reference,
                Facility = f.Code,
                s.StallNo,
                PayorName = u != null ? u.FullName : null,
                BillingYear = t.TargetYear!.Value,
                BillingMonth = t.TargetMonth!.Value,
                t.Amount,
                t.Method,
                t.PaidAt
            }).ToListAsync(ct);

        // ── NPM utility-bill targets: derive stall/period from the target, awaiting while the bill still
        //    has a utility without an OR (elec or water not yet receipted). ──
        var npmUtilRows = await (
            from t in context.OnlinePaymentTransactions
            where t.Status == OnlinePaymentStatus.Paid && t.TargetKind == OnlinePaymentTargetKind.NpmUtilityBill
            where year == null || (t.TargetYear == year && t.TargetMonth == month)
            join s in context.Stalls on t.TargetStallId equals (Guid?)s.Id
            join f in context.Facilities on s.FacilityId equals f.Id
            join u in context.PayorUsers on t.PayorUserId equals u.Id into payor
            from u in payor.DefaultIfEmpty()
            where context.UtilityBills.Any(b => b.StallId == t.TargetStallId
                && b.BillingYear == t.TargetYear
                && b.BillingMonth == t.TargetMonth
                && (b.ElecORNumber == null || b.WaterORNumber == null))
            select new
            {
                t.Id,
                t.Reference,
                Facility = f.Code,
                s.StallNo,
                PayorName = u != null ? u.FullName : null,
                BillingYear = t.TargetYear!.Value,
                BillingMonth = t.TargetMonth!.Value,
                t.Amount,
                t.Method,
                t.PaidAt
            }).ToListAsync(ct);

        // ── NPM fish-DAY targets: derive stall/period from the target, awaiting while THAT specific day's
        //    collection is paid but still without an OR. ──
        var npmFishDayRows = await (
            from t in context.OnlinePaymentTransactions
            where t.Status == OnlinePaymentStatus.Paid && t.TargetKind == OnlinePaymentTargetKind.NpmFishDay
            where year == null || (t.TargetYear == year && t.TargetMonth == month)
            join s in context.Stalls on t.TargetStallId equals (Guid?)s.Id
            join f in context.Facilities on s.FacilityId equals f.Id
            join u in context.PayorUsers on t.PayorUserId equals u.Id into payor
            from u in payor.DefaultIfEmpty()
            where context.DailyCollections.Any(d => d.StallId == t.TargetStallId
                && d.CollectionDate.Year == t.TargetYear
                && d.CollectionDate.Month == t.TargetMonth
                && d.CollectionDate.Day == t.TargetDay
                && d.IsPaid
                && (d.ORNumber == null || d.ORNumber == ""))
            select new
            {
                t.Id,
                t.Reference,
                Facility = f.Code,
                s.StallNo,
                PayorName = u != null ? u.FullName : null,
                BillingYear = t.TargetYear!.Value,
                BillingMonth = t.TargetMonth!.Value,
                t.Amount,
                t.Method,
                t.PaidAt
            }).ToListAsync(ct);

        return monthlyRows.Concat(npmRows).Concat(npmUtilRows).Concat(npmFishDayRows)
            .OrderBy(x => x.PaidAt)
            .Select(x => new OnlinePaymentAwaitingOrDto(
                x.Id,
                x.Reference,
                x.Facility,
                x.StallNo,
                x.PayorName ?? "—",
                $"{x.BillingYear:0000}-{x.BillingMonth:00}",
                x.Amount,
                x.Method,
                x.PaidAt))
            .ToList();
    }

    public async Task<OnlinePaymentDashboardDto> GetDashboardAsync(int year, int month, int recentLimit, CancellationToken ct = default)
    {
        // Treasury figures use Philippine-time period boundaries (PaidAt is stored UTC).
        var (monthStartUtc, _) = PhilippineTime.DayUtcRange(new DateOnly(year, month, 1));
        var (nextMonthUtc, _) = PhilippineTime.DayUtcRange(new DateOnly(year, month, 1).AddMonths(1));
        var (yearStartUtc, _) = PhilippineTime.DayUtcRange(new DateOnly(year, 1, 1));
        var (nextYearUtc, _) = PhilippineTime.DayUtcRange(new DateOnly(year, 1, 1).AddYears(1));

        // "Settled" = money actually received (Paid, or OR-completed). Tenant-scoped by the global filter.
        var settled = context.OnlinePaymentTransactions
            .Where(t => t.Status == OnlinePaymentStatus.Paid || t.Status == OnlinePaymentStatus.Completed);

        var collectedThisMonth = await settled
            .Where(t => t.PaidAt >= monthStartUtc && t.PaidAt < nextMonthUtc)
            .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

        var yearSettled = settled.Where(t => t.PaidAt >= yearStartUtc && t.PaidAt < nextYearUtc);
        var collectedThisYear = await yearSettled.SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;
        var settledCountThisYear = await yearSettled.CountAsync(ct);

        var topMethod = await yearSettled
            .Where(t => t.Method != null)
            .GroupBy(t => t.Method!)
            .Select(g => new { Method = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Select(x => x.Method)
            .FirstOrDefaultAsync(ct);

        // Recent received payments with payor + facility resolved. Monthly-rental targets resolve the
        // facility/period via their PaymentRecord; NPM targets carry the stall + billing period directly.
        // Both projections share an identical shape so they compose into one chronological list.
        var monthlyRows = await (
            from t in settled.Where(t => t.TargetKind == OnlinePaymentTargetKind.MonthlyRecord)
            join r in context.PaymentRecords on t.PaymentRecordId equals (Guid?)r.Id
            join s in context.Stalls on r.StallId equals s.Id
            join f in context.Facilities on s.FacilityId equals f.Id
            join u in context.PayorUsers on t.PayorUserId equals u.Id into payor
            from u in payor.DefaultIfEmpty()
            select new
            {
                t.Id,
                t.Reference,
                Facility = f.Name,
                s.StallNo,
                PayorName = u != null ? u.FullName : null,
                Year = r.BillingYear,
                Month = r.BillingMonth,
                t.Amount,
                t.Method,
                t.Status,
                t.ORNumber,
                t.PaidAt
            }).ToListAsync(ct);

        var npmRows = await (
            from t in settled.Where(t => t.TargetKind != OnlinePaymentTargetKind.MonthlyRecord && t.TargetStallId != null)
            join s in context.Stalls on t.TargetStallId equals (Guid?)s.Id
            join f in context.Facilities on s.FacilityId equals f.Id
            join u in context.PayorUsers on t.PayorUserId equals u.Id into payor
            from u in payor.DefaultIfEmpty()
            select new
            {
                t.Id,
                t.Reference,
                Facility = f.Name,
                s.StallNo,
                PayorName = u != null ? u.FullName : null,
                Year = t.TargetYear!.Value,
                Month = t.TargetMonth!.Value,
                t.Amount,
                t.Method,
                t.Status,
                t.ORNumber,
                t.PaidAt
            }).ToListAsync(ct);

        var recent = monthlyRows.Concat(npmRows)
            .OrderByDescending(x => x.PaidAt ?? DateTime.MinValue)
            .Take(recentLimit)
            .Select(x => new OnlinePaymentHistoryItemDto(
                x.Reference,
                x.PayorName ?? "—",
                x.Facility,
                x.StallNo,
                $"{x.Year:0000}-{x.Month:00}",
                x.Amount,
                x.Method,
                x.Status == OnlinePaymentStatus.Completed ? "Completed" : "Awaiting OR",
                x.ORNumber,
                x.PaidAt))
            .ToList();

        return new OnlinePaymentDashboardDto(
            collectedThisMonth, collectedThisYear, settledCountThisYear, topMethod, year, recent);
    }
}
