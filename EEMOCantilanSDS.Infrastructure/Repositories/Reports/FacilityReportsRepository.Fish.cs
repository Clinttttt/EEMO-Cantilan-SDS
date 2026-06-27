using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of FacilityReportsRepository: per-stall NPM fish-kilo aggregation for the Export-Data
// collection report. Reuses the same recognition rules as the fee-type breakdown so the per-stall
// fish fees reconcile to the section/facility fish totals shown elsewhere.
public partial class FacilityReportsRepository
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> GetNpmFishKilosByStallAsync(
        int year, int month, CancellationToken ct)
    {
        var facility = await _context.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == FacilityCode.NPM, ct);
        if (facility is null)
            return new Dictionary<Guid, decimal>();

        var startDate = new DateOnly(year, month, 1);
        var endDate = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var npmStalls = await LoadNpmRevenueStallsAsync(facility.Id, ct);
        var npmStallsById = npmStalls.ToDictionary(s => s.Id);
        var npmStallIds = npmStallsById.Keys.ToList();

        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => npmStallIds.Contains(pr.StallId))
            .ToListAsync(ct);

        var periodPaymentRecords = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .Where(pr => pr.Status is PaymentStatus.Paid or PaymentStatus.Partial)
            .ToList();

        // A monthly payment supersedes daily collections for that stall (same rule as the breakdown).
        var stallsWithMonthlyPayments = periodPaymentRecords
            .Select(pr => pr.StallId)
            .ToHashSet();

        var dailyCollections = await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => npmStallIds.Contains(dc.StallId)
                && dc.CollectionDate >= startDate
                && dc.CollectionDate <= endDate
                && dc.IsPaid
                && !stallsWithMonthlyPayments.Contains(dc.StallId))
            .ToListAsync(ct);

        var result = new Dictionary<Guid, decimal>();

        // Daily-collection fish kilos (collectable days only), for stalls without a monthly payment.
        foreach (var dc in dailyCollections)
        {
            if (!npmStallsById.TryGetValue(dc.StallId, out var stall)
                || !IsUnderContractOn(stall, dc.CollectionDate))
                continue;
            var kilos = dc.FishKilos ?? 0m;
            if (kilos <= 0m) continue;
            result[dc.StallId] = result.GetValueOrDefault(dc.StallId) + kilos;
        }

        // Whole-month paid monthly records' fish kilos.
        foreach (var pr in periodPaymentRecords
            .Where(pr => pr.Status == PaymentStatus.Paid && IsWholeBillingMonthSelected(pr, startDate, endDate)))
        {
            var kilos = pr.FishKilos ?? 0m;
            if (kilos <= 0m) continue;
            result[pr.StallId] = result.GetValueOrDefault(pr.StallId) + kilos;
        }

        return result;
    }
}
