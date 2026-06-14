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
        return await context.OnlinePaymentTransactions
            .FirstOrDefaultAsync(t => t.GatewayReference == gatewayReference, ct);
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

    public async Task<IReadOnlyList<OnlinePaymentAwaitingOrDto>> GetAwaitingOrAsync(CancellationToken ct = default)
    {
        // Project the joined fields, then format the period in memory (string interpolation with
        // padding does not translate to SQL).
        var rows = await (
            from t in context.OnlinePaymentTransactions
            where t.Status == OnlinePaymentStatus.Paid
            join r in context.PaymentRecords on t.PaymentRecordId equals r.Id
            // Skip any whose OR is already on the ledger record (e.g. encoded from the mobile side) so the
            // same payment can't show up here as still needing an OR.
            where r.ORNumber == null
            join s in context.Stalls on r.StallId equals s.Id
            join f in context.Facilities on s.FacilityId equals f.Id
            join u in context.PayorUsers on t.PayorUserId equals u.Id into payor
            from u in payor.DefaultIfEmpty()
            orderby t.PaidAt
            select new
            {
                t.Id,
                t.Reference,
                Facility = f.Code,
                s.StallNo,
                PayorName = u != null ? u.FullName : null,
                r.BillingYear,
                r.BillingMonth,
                t.Amount,
                t.Method,
                t.PaidAt
            }).ToListAsync(ct);

        return rows.Select(x => new OnlinePaymentAwaitingOrDto(
            x.Id,
            x.Reference,
            x.Facility,
            x.StallNo,
            x.PayorName ?? "—",
            $"{x.BillingYear:0000}-{x.BillingMonth:00}",
            x.Amount,
            x.Method,
            x.PaidAt)).ToList();
    }
}
