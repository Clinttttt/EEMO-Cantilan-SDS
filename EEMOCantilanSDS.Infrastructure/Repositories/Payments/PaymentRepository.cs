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

// Entry partial of PaymentRepository: the payment AGGREGATE, the per-facility record reads, and the receipt-number
// availability checks (IPaymentRepository). The narrow capabilities live in sibling files - .Ledger.cs (one account's
// history, summary and outstanding months) and .MissingReceipts.cs (money taken whose OR is still blank).
//
// One class in three files, not three classes: those reads share the private obligation arithmetic that decides which market
// days a stall is chargeable for, and duplicating it would let one account's ledger disagree with the office's reports. What
// IS separate is the contracts, so a caller that only wants to read one account depends on IStallLedgerQueries and nothing
// else.
public partial class PaymentRepository(AppDbContext context, IFeeRateResolver feeRateResolver, IClock clock)
    : IPaymentRepository, IStallLedgerQueries, IMissingReceiptQueries
{
    /// <summary>
    /// Test/non-DI convenience: resolves fees from the context (empty rate table => ordinance constants) and reads the real
    /// _clock.
    ///
    /// <para>
    /// A test that cares WHICH DAY it is should use the full constructor and pass a fixed _clock. Everything this repository
    /// decides about eligibility — which market days are chargeable, who holds a stall now, which rate applies — is a
    /// question about "today", so a test using this overload is agreeing to be asked on whatever day it runs.
    /// </para>
    /// </summary>
    public PaymentRepository(AppDbContext context) : this(context, new FeeRateResolver(context), new SystemClock()) { }

    /// <summary>
    /// Captured into fields because this class is PARTIAL: a primary-constructor parameter is only in scope in the file that
    /// declares it, and the reads that need the context, the rates and "today" are spread across the sibling files. The same
    /// convention <see cref="Reports.FacilityReportsRepository"/>, <see cref="CollectorRepository"/> and
    /// <see cref="StallRepository"/> already use.
    /// </summary>
    private readonly AppDbContext _context = context;

    private readonly IFeeRateResolver _feeRateResolver = feeRateResolver;
    private readonly IClock _clock = clock;

    public async Task<PaymentRecord?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _context.PaymentRecords.FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<PaymentRecordDto?> GetPaymentRecordAsync(Guid stallId, int year, int month, CancellationToken ct)
    {
        var payment = await _context.PaymentRecords
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
        var payments = await _context.PaymentRecords
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
        var today = _clock.PhilippineToday;
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Daily collections for the facility this month — paid days drive the operational stats, and
        // today's absent marker drives the "Absent" pill. NPM status is daily (not monthly records).
        var collections = await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.Stall!.Facility!.Code == facilityCode
                && (dc.IsPaid || dc.IsAbsent)
                && dc.CollectionDate >= monthStart
                && dc.CollectionDate <= monthEnd)
            .Select(dc => new { dc.StallId, dc.CollectionDate, dc.ORNumber, dc.IsPaid, dc.IsAbsent })
            .ToListAsync(ct);

        // Current-month electricity+water bill status per stall. Status is a computed (derived) property,
        // so bills are materialised first and the status computed in memory. One bill per stall/month.
        var utilityByStall = (await _context.UtilityBills
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
        await _context.PaymentRecords.AddAsync(payment, ct);
    }

    public async Task UpdateAsync(PaymentRecord payment, CancellationToken ct)
    {
        _context.PaymentRecords.Update(payment);
        await Task.CompletedTask;
    }
}

