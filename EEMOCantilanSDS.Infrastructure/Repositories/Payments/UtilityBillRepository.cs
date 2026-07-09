using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Payments;

public class UtilityBillRepository(AppDbContext context) : IUtilityBillRepository
{
    // Tracked (the caller mutates and the change is saved via IUnitOfWork).
    public async Task<UtilityBill?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await context.UtilityBills.FirstOrDefaultAsync(b => b.Id == id, ct);

    public async Task<UtilityBill?> GetByStallAndMonthAsync(Guid stallId, int year, int month, CancellationToken ct = default) =>
        await context.UtilityBills.FirstOrDefaultAsync(
            b => b.StallId == stallId && b.BillingYear == year && b.BillingMonth == month, ct);

    public async Task<UtilityBill?> GetLatestBeforeAsync(Guid stallId, int year, int month, CancellationToken ct = default) =>
        await context.UtilityBills.AsNoTracking()
            .Where(b => b.StallId == stallId
                && (b.BillingYear < year || (b.BillingYear == year && b.BillingMonth < month)))
            .OrderByDescending(b => b.BillingYear).ThenByDescending(b => b.BillingMonth)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<UtilityBill>> GetForMonthAsync(int year, int month, CancellationToken ct = default) =>
        await context.UtilityBills.AsNoTracking()
            .Include(b => b.Stall)
                .ThenInclude(s => s!.Contracts)
            .Where(b => b.BillingYear == year && b.BillingMonth == month)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UtilityBill>> GetAllForStallAsync(Guid stallId, CancellationToken ct = default) =>
        await context.UtilityBills.AsNoTracking()
            .Where(b => b.StallId == stallId)
            .OrderByDescending(b => b.BillingYear).ThenByDescending(b => b.BillingMonth)
            .ToListAsync(ct);

    public async Task AddAsync(UtilityBill bill, CancellationToken ct = default) =>
        await context.UtilityBills.AddAsync(bill, ct);

    public async Task<bool> IsORNumberUniqueAsync(string orNumber, Guid? excludeBillId = null, CancellationToken ct = default)
    {
        var trimmed = (orNumber ?? string.Empty).Trim();
        if (trimmed.Length == 0) return true;

        // OR (receipt) numbers must stay unique across EVERY module in this LGU (bypass the soft-delete filter
        // so a deleted row's OR can't be reused, and scope to the current municipality). This utility bill is
        // excluded so re-marking it (or one OR covering both its utilities) is allowed.
        var mid = context.CurrentMunicipalityId;
        if (await context.UtilityBills.IgnoreQueryFilters().AnyAsync(b => (mid == Guid.Empty || b.MunicipalityId == mid)
                && (excludeBillId == null || b.Id != excludeBillId)
                && ((b.ElecORNumber != null && b.ElecORNumber == trimmed) || (b.WaterORNumber != null && b.WaterORNumber == trimmed)), ct)) return false;
        if (await context.PaymentRecords.IgnoreQueryFilters().AnyAsync(p => (mid == Guid.Empty || p.MunicipalityId == mid) && p.ORNumber == trimmed, ct)) return false;
        if (await context.DailyCollections.IgnoreQueryFilters().AnyAsync(d => (mid == Guid.Empty || d.MunicipalityId == mid) && d.ORNumber == trimmed, ct)) return false;
        if (await context.SlaughterTransactions.IgnoreQueryFilters().AnyAsync(s => (mid == Guid.Empty || s.MunicipalityId == mid) && s.ORNumber == trimmed, ct)) return false;
        if (await context.TpmAttendances.IgnoreQueryFilters().AnyAsync(a => (mid == Guid.Empty || a.MunicipalityId == mid) && a.ORNumber == trimmed, ct)) return false;
        if (await context.TrmTrips.IgnoreQueryFilters().AnyAsync(t => (mid == Guid.Empty || t.MunicipalityId == mid) && t.ORNumber == trimmed, ct)) return false;
        return true;
    }
}
