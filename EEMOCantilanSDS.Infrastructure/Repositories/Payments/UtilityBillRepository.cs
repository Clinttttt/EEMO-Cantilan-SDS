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
        return !await context.UtilityBills.AsNoTracking()
            .AnyAsync(b => (excludeBillId == null || b.Id != excludeBillId)
                        && ((b.ElecORNumber != null && b.ElecORNumber == trimmed)
                         || (b.WaterORNumber != null && b.WaterORNumber == trimmed)), ct);
    }
}
