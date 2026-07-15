using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
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

    public async Task<OnlinePaymentTransaction?> GetResumableNpmTransactionAsync(Guid stallId, int year, int month, CancellationToken ct = default)
    {
        return await context.OnlinePaymentTransactions
            .Where(t => t.TargetKind == OnlinePaymentTargetKind.NpmDailyMonth
                && t.TargetStallId == stallId && t.TargetYear == year && t.TargetMonth == month
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

        return monthlyRows.Concat(npmRows)
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
}
