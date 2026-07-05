using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of FacilityReportsRepository: collection-rate, occupancy, and pending-payment helpers.
public partial class FacilityReportsRepository
{
    #region Collection Rate Helpers

    /// <summary>
    /// Calculates collection rate as (amount collected / amount assessed) * 100.
    /// NPM daily collections are assessed by active collection days instead of adding a
    /// second monthly rental denominator on top of the same daily fee.
    /// </summary>
    private async Task<decimal> CalculateCollectionRateAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var occupiedStalls = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId
               
                && s.Contracts.Any(c => c.IsActive))
            .Select(s => new { s.Id, s.MonthlyRate })
            .ToListAsync(ct);

        if (occupiedStalls.Count == 0)
            return 0m;

        var occupiedStallIds = occupiedStalls.Select(s => s.Id).ToList();

        if (facilityCode == FacilityCode.NPM)
        {
            var npmCollectableStalls = await LoadNpmCollectableStallsAsync(facilityId, ct);
            var npmStallsById = npmCollectableStalls.ToDictionary(s => s.Id);
            var npmStallIds = npmStallsById.Keys.ToList();
            var expectedDailyFees = CalculateNpmExpectedDailyFeeRevenue(npmCollectableStalls, startDate, endDate);

            var npmPaymentRecords = await _context.PaymentRecords
                .AsNoTracking()
                .Where(pr => npmStallIds.Contains(pr.StallId))
                .ToListAsync(ct);

            var monthlyDailyFeeRevenue = npmPaymentRecords.Sum(pr => npmStallsById.TryGetValue(pr.StallId, out var stall)
                ? RecognizedNpmDailyFeeRevenue(pr, startDate, endDate, stall)
                : 0m);

            var stallsWithMonthlyPayments = npmPaymentRecords
                .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate)
                    && pr.Status != PaymentStatus.Unpaid)
                .Select(pr => pr.StallId)
                .ToHashSet();

            var dailyCollections = await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => npmStallIds.Contains(dc.StallId)
                   
                    && dc.IsPaid
                    && dc.CollectionDate >= startDate
                    && dc.CollectionDate <= endDate
                    && !stallsWithMonthlyPayments.Contains(dc.StallId))
                .ToListAsync(ct);

            var dailyFeeRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                && IsStallCollectableOn(stall, dc.CollectionDate)
                    ? dc.DailyFee
                    : 0m);

            if (expectedDailyFees == 0)
                return 0m;

            return Math.Min(100m, ((monthlyDailyFeeRevenue + dailyFeeRevenue) / expectedDailyFees) * 100m);
        }

        var allPaymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => occupiedStallIds.Contains(pr.StallId))
            .ToListAsync(ct);

        // Collected = recognized rent payments in the period (Paid → full bill; Partial → partial).
        var totalCollected = allPaymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .Sum(pr => RecognizedRevenue(pr, includeFish: false));

        // Assessed = full monthly-rental obligation that has come DUE for EVERY occupied stall,
        // including those with no PaymentRecord yet (an unpaid stall still owes). Uses the same
        // due-month rule as the compliance balance so the rate and the Outstanding KPI reconcile.
        var occupiedStallEntities = await LoadOccupiedStallsAsync(facilityId, ct);
        var totalAssessed = occupiedStallEntities.Sum(s => CalculateStallRentObligationDue(s, startDate, endDate));

        if (totalAssessed == 0)
            return 0m;

        return Math.Min(100m, (totalCollected / totalAssessed) * 100m);
    }

    #endregion

    #region Occupancy Helpers

    /// <summary>
    /// Counts stalls with active contracts.
    /// </summary>
    private async Task<int> CalculateOccupiedStallsAsync(
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        // "Occupied" is period-scoped: a stall counts only if its contract was effective during
        // the selected period. A stall whose contract starts after the period (or expired before
        // it) is not occupied for that period, so it must not inflate the occupancy card.
        var stalls = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .Where(s => s.FacilityId == facilityId
               
                && s.Contracts.Any(c => c.IsActive))
            .ToListAsync(ct);

        return stalls.Count(s => CountNpmCollectableDays(s, startDate, endDate) > 0);
    }

    private async Task<int> CalculateTotalStallsAsync(
        Guid facilityId,
        CancellationToken ct)
    {
        return await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId)
            .CountAsync(ct);
    }

    #endregion

    #region Pending Payment Helpers

    /// <summary>
    /// Calculates pending payment count and amount (Unpaid + Partial status).
    /// For NPM: Subtracts daily collections from the pending amount.
    /// Counts ALL occupied stalls (with active contracts), not just those with payment records.
    /// Uses the stall's MonthlyRate property for expected bill calculation.
    /// </summary>
    private async Task<(int count, decimal amount)> CalculatePendingPaymentsAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        if (facilityCode == FacilityCode.NPM)
        {
            var compliance = await GenerateStallComplianceAsync(facilityCode, facilityId, startDate, endDate, ct);
            return (compliance.Count(s => s.Balance > 0m), compliance.Sum(s => s.Balance));
        }

        // Get all OCCUPIED stalls (with active contracts) including their MonthlyRate
        var occupiedStalls = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId 
               
                && s.Contracts.Any(c => c.IsActive))
            .Select(s => new { s.Id, s.MonthlyRate })
            .ToListAsync(ct);

        if (occupiedStalls.Count == 0)
        {
            return (0, 0m);
        }

        // Local copy of the resolved fish rate so it embeds cleanly as a query parameter (EF) and
        // in the in-memory bill math below. Defaults to the ordinance constant for Cantilan.
        var npmFish = _npmFishRate;

        // Get all payment records and filter in memory
        var allPaymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => occupiedStalls.Select(s => s.Id).Contains(pr.StallId))
            .ToListAsync(ct);

        var paymentsByStall = allPaymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .GroupBy(pr => pr.StallId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1)).First()
            );

        // NPM also collects daily fees; aggregate them once, server-side.
        var dailyCollectionsByStall = facilityCode == FacilityCode.NPM
            ? await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => occupiedStalls.Select(s => s.Id).Contains(dc.StallId)
                   
                    && dc.IsPaid
                    && dc.CollectionDate >= startDate
                    && dc.CollectionDate <= endDate)
                .GroupBy(dc => dc.StallId)
                .Select(g => new
                {
                    StallId = g.Key,
                    TotalCollected = g.Sum(dc => dc.DailyFee
                        + (dc.FishKilos.HasValue ? dc.FishKilos.Value * npmFish : 0m))
                })
                .ToDictionaryAsync(x => x.StallId, x => x.TotalCollected, ct)
            : new Dictionary<Guid, decimal>();

        // Count occupied stalls that still owe money (Unpaid or Partial) and the outstanding total.
        var count = 0;
        var amount = 0m;

        foreach (var stall in occupiedStalls)
        {
            decimal totalBill;
            decimal amountPaid;

            if (paymentsByStall.TryGetValue(stall.Id, out var pr))
            {
                totalBill = pr.BaseRentalAmount
                    + (pr.ElecAmount ?? 0)
                    + (pr.WaterAmount ?? 0)
                    + (pr.FishKilos.HasValue ? pr.FishKilos.Value * npmFish : 0);

                amountPaid = pr.Status == PaymentStatus.Paid
                    ? totalBill
                    : pr.Status == PaymentStatus.Partial
                        ? pr.PartialAmount
                        : 0;
            }
            else
            {
                // No payment record: use the stall's MonthlyRate as the expected bill
                totalBill = stall.MonthlyRate;
                amountPaid = 0m;
            }

            amountPaid += dailyCollectionsByStall.GetValueOrDefault(stall.Id);

            if (amountPaid < totalBill)
            {
                count++;
                amount += totalBill - amountPaid;
            }
        }

        return (count, amount);
    }

    #endregion

}
