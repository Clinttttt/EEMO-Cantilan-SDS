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

// Partial of FacilityReportsRepository: revenue recognition/calculation helpers.
public partial class FacilityReportsRepository
{
    #region Revenue Calculation Helpers

    /// <summary>
    /// Checks if a payment record falls within the date range based on billing year and month.
    /// </summary>
    private static bool IsPaymentInDateRange(int billingYear, int billingMonth, DateOnly startDate, DateOnly endDate)
    {
        var billingDate = new DateOnly(billingYear, billingMonth, 1);
        var rangeStart = new DateOnly(startDate.Year, startDate.Month, 1);
        var rangeEnd = new DateOnly(endDate.Year, endDate.Month, 1);
        
        return billingDate >= rangeStart && billingDate <= rangeEnd;
    }

    /// <summary>
    /// Revenue recognized (actually collected) from a single payment record.
    /// Paid → base rental + utilities (+ fish fee when <paramref name="includeFish"/>);
    /// Partial → the partial amount; Unpaid → nothing.
    /// </summary>
    private static decimal RecognizedRevenue(PaymentRecord pr, bool includeFish) => pr.Status switch
    {
        PaymentStatus.Paid => pr.BaseRentalAmount
            + (pr.ElecAmount ?? 0)
            + (pr.WaterAmount ?? 0)
            + (includeFish && pr.FishKilos.HasValue ? pr.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m),
        PaymentStatus.Partial => pr.PartialAmount,
        _ => 0m
    };

    private static decimal CalculateNpmDailyObligation(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            return 0m;

        return (endDate.DayNumber - startDate.DayNumber + 1) * FeeRates.NpmDailyFee;
    }

    private static bool IsContractCollectableOn(Contract contract, DateOnly date)
        => contract.IsActive
            && contract.EffectivityDate <= date
            && date <= contract.ExpiryDate;

    private static bool IsStallCollectableOn(Stall stall, DateOnly date)
        => stall.Status == StallStatus.Active
            && stall.Contracts.Any(c => IsContractCollectableOn(c, date));

    private static int CountNpmCollectableDays(Stall stall, DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            return 0;

        var days = 0;
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (IsStallCollectableOn(stall, date))
                days++;
        }

        return days;
    }

    private static decimal CalculateNpmDailyObligation(Stall stall, DateOnly startDate, DateOnly endDate)
        => CountNpmCollectableDays(stall, startDate, endDate) * FeeRates.NpmDailyFee;

    private async Task<List<Stall>> LoadNpmCollectableStallsAsync(Guid facilityId, CancellationToken ct)
    {
        return await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .Where(s => s.FacilityId == facilityId
                && s.Status == StallStatus.Active
               
                && s.Contracts.Any(c => c.IsActive))
            .ToListAsync(ct);
    }

    private static decimal CalculateNpmExpectedDailyFeeRevenue(IEnumerable<Stall> stalls, DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            return 0m;

        return stalls.Sum(s => CalculateNpmDailyObligation(s, startDate, endDate));
    }

    private static decimal CalculateNpmSelectedBill(PaymentRecord pr, DateOnly startDate, DateOnly endDate)
    {
        var bill = CalculateNpmDailyObligation(
            startDate > new DateOnly(pr.BillingYear, pr.BillingMonth, 1) ? startDate : new DateOnly(pr.BillingYear, pr.BillingMonth, 1),
            endDate < new DateOnly(pr.BillingYear, pr.BillingMonth, DateTime.DaysInMonth(pr.BillingYear, pr.BillingMonth))
                ? endDate
                : new DateOnly(pr.BillingYear, pr.BillingMonth, DateTime.DaysInMonth(pr.BillingYear, pr.BillingMonth)));

        if (!IsWholeBillingMonthSelected(pr, startDate, endDate))
            return bill;

        return bill
            + (pr.FishKilos.HasValue ? pr.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m);
    }

    private static decimal CalculateNpmAdditionalCharges(PaymentRecord pr, DateOnly startDate, DateOnly endDate)
    {
        if (!IsWholeBillingMonthSelected(pr, startDate, endDate))
            return 0m;

        return pr.FishKilos.HasValue ? pr.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m;
    }

    private static decimal RecognizedNpmPaymentRevenue(PaymentRecord pr, DateOnly startDate, DateOnly endDate, Stall? stall = null)
    {
        if (pr.Status == PaymentStatus.Unpaid || !IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            return 0m;

        var monthStart = new DateOnly(pr.BillingYear, pr.BillingMonth, 1);
        var monthEnd = new DateOnly(pr.BillingYear, pr.BillingMonth, DateTime.DaysInMonth(pr.BillingYear, pr.BillingMonth));
        var overlapStart = startDate > monthStart ? startDate : monthStart;
        var overlapEnd = endDate < monthEnd ? endDate : monthEnd;

        if (overlapEnd < overlapStart)
            return 0m;

        if (stall is not null && CountNpmCollectableDays(stall, overlapStart, overlapEnd) == 0)
            return 0m;

        var dailyRevenue = RecognizedNpmDailyFeeRevenue(pr, startDate, endDate, stall);

        if (!IsWholeBillingMonthSelected(pr, startDate, endDate) || pr.Status != PaymentStatus.Paid)
            return dailyRevenue;

        return dailyRevenue
            + (pr.FishKilos.HasValue ? pr.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m);
    }

    private static bool IsWholeBillingMonthSelected(PaymentRecord pr, DateOnly startDate, DateOnly endDate)
    {
        var monthStart = new DateOnly(pr.BillingYear, pr.BillingMonth, 1);
        var monthEnd = new DateOnly(pr.BillingYear, pr.BillingMonth, DateTime.DaysInMonth(pr.BillingYear, pr.BillingMonth));
        return startDate <= monthStart && endDate >= monthEnd;
    }

    private static decimal RecognizedNpmDailyFeeRevenue(PaymentRecord pr, DateOnly startDate, DateOnly endDate, Stall? stall = null)
    {
        if (pr.Status == PaymentStatus.Unpaid || !IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            return 0m;

        var monthStart = new DateOnly(pr.BillingYear, pr.BillingMonth, 1);
        var monthEnd = new DateOnly(pr.BillingYear, pr.BillingMonth, DateTime.DaysInMonth(pr.BillingYear, pr.BillingMonth));
        var overlapStart = startDate > monthStart ? startDate : monthStart;
        var overlapEnd = endDate < monthEnd ? endDate : monthEnd;

        if (overlapEnd < overlapStart)
            return 0m;

        var paidTowardDailyFee = pr.Status == PaymentStatus.Paid
            ? pr.BaseRentalAmount
            : Math.Min(pr.PartialAmount, pr.BaseRentalAmount);

        if (stall is not null)
            return AllocatePrepaidDailyAmountToCollectableRange(paidTowardDailyFee, stall, monthStart, overlapStart, overlapEnd);

        return AllocatePrepaidDailyAmountToRange(paidTowardDailyFee, monthStart, overlapStart, overlapEnd);
    }

    private static decimal AllocatePrepaidDailyAmountToCollectableRange(
        decimal prepaidAmount,
        Stall stall,
        DateOnly monthStart,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        if (prepaidAmount <= 0m || FeeRates.NpmDailyFee <= 0m || rangeEnd < rangeStart)
            return 0m;

        var monthEnd = new DateOnly(monthStart.Year, monthStart.Month, DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
        var collectableDays = new List<DateOnly>();
        for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
        {
            if (IsStallCollectableOn(stall, date))
                collectableDays.Add(date);
        }

        var fullCoveredDays = (int)Math.Floor(prepaidAmount / FeeRates.NpmDailyFee);
        var remainder = prepaidAmount % FeeRates.NpmDailyFee;
        var amount = collectableDays
            .Take(fullCoveredDays)
            .Where(d => d >= rangeStart && d <= rangeEnd)
            .Sum(_ => FeeRates.NpmDailyFee);

        if (remainder > 0m && collectableDays.Count > fullCoveredDays)
        {
            var remainderDay = collectableDays[fullCoveredDays];
            if (remainderDay >= rangeStart && remainderDay <= rangeEnd)
                amount += remainder;
        }

        return amount;
    }

    private static decimal AllocatePrepaidDailyAmountToRange(
        decimal prepaidAmount,
        DateOnly monthStart,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        if (prepaidAmount <= 0m || FeeRates.NpmDailyFee <= 0m || rangeEnd < rangeStart)
            return 0m;

        var fullCoveredDays = (int)Math.Floor(prepaidAmount / FeeRates.NpmDailyFee);
        var remainder = prepaidAmount % FeeRates.NpmDailyFee;
        var rangeStartIndex = rangeStart.DayNumber - monthStart.DayNumber;
        var rangeEndIndex = rangeEnd.DayNumber - monthStart.DayNumber;

        var firstFullIndex = 0;
        var lastFullIndex = fullCoveredDays - 1;
        var fullDaysInRange = lastFullIndex >= firstFullIndex
            ? Math.Max(0, Math.Min(rangeEndIndex, lastFullIndex) - Math.Max(rangeStartIndex, firstFullIndex) + 1)
            : 0;

        var amount = fullDaysInRange * FeeRates.NpmDailyFee;
        var remainderDayIndex = fullCoveredDays;
        if (remainder > 0m && rangeStartIndex <= remainderDayIndex && remainderDayIndex <= rangeEndIndex)
            amount += remainder;

        return amount;
    }

    /// <summary>
    /// Calculates total revenue for NPM facility (daily collections + monthly payments).
    /// Only counts Paid and Partial payments.
    /// </summary>
    private async Task<decimal> CalculateNpmRevenueAsync(
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var npmCollectableStalls = await LoadNpmCollectableStallsAsync(facilityId, ct);
        var npmStallsById = npmCollectableStalls.ToDictionary(s => s.Id);
        var npmStallIds = npmStallsById.Keys.ToList();

        // Get all payment records and filter in memory
        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => npmStallIds.Contains(pr.StallId))
            .ToListAsync(ct);

        var monthlyRevenue = paymentRecords.Sum(pr => npmStallsById.TryGetValue(pr.StallId, out var stall)
            ? RecognizedNpmPaymentRevenue(pr, startDate, endDate, stall)
            : 0m);

        // Get stalls that have monthly payments in this period (exclude from daily count)
        var stallsWithMonthlyPayments = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate)
                && pr.Status != PaymentStatus.Unpaid)
            .Select(pr => pr.StallId)
            .ToHashSet();

        // Sum daily collections ONLY for stalls without monthly payments
        var dailyCollections = await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => npmStallIds.Contains(dc.StallId)
                && dc.CollectionDate >= startDate
                && dc.CollectionDate <= endDate
                && dc.IsPaid
               
                && !stallsWithMonthlyPayments.Contains(dc.StallId))
            .ToListAsync(ct);

        var dailyRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
            && IsStallCollectableOn(stall, dc.CollectionDate)
                ? dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m)
                : 0m);

        return dailyRevenue + monthlyRevenue;
    }

    /// <summary>
    /// Calculates total revenue for non-NPM facilities (monthly payments only).
    /// Only counts Paid and Partial payments.
    /// </summary>
    private async Task<decimal> CalculateMonthlyRentalRevenueAsync(
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        // Get all payment records and filter in memory
        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => pr.Stall!.FacilityId == facilityId)
            .ToListAsync(ct);

        return paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .Sum(pr => RecognizedRevenue(pr, includeFish: false));
    }

    /// <summary>
    /// Loads occupied stalls (active contract) for a non-NPM facility, including their active
    /// contracts, so the monthly-rental obligation can be assessed independently of whether a
    /// PaymentRecord exists for a given month (an unpaid stall has no record but still owes rent).
    /// </summary>
    private async Task<List<Stall>> LoadOccupiedStallsAsync(Guid facilityId, CancellationToken ct)
    {
        return await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .Where(s => s.FacilityId == facilityId
                && s.Status == StallStatus.Active
               
                && s.Contracts.Any(c => c.IsActive))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Rent obligation that has actually come DUE for a single stall within [start, end]: the
    /// stall's MonthlyRate for every month its active contract is effective AND that has already
    /// started (future months in the current period are not yet owed). This mirrors the month rule
    /// in <see cref="CountMissedMonths"/>, so the compliance Balance, Outstanding KPI, Collection
    /// Rate and Missed-months all reconcile (e.g. Balance of an unpaid stall == MissedMonths × rate).
    /// </summary>
    private static decimal CalculateStallRentObligationDue(Stall stall, DateOnly start, DateOnly end)
    {
        if (end < start) return 0m;
        var today = PhilippineTime.Today;
        decimal total = 0m;
        var cursor = new DateOnly(start.Year, start.Month, 1);
        var last = new DateOnly(end.Year, end.Month, 1);
        while (cursor <= last)
        {
            var mStart = cursor;
            var mEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
            // Skip months that have not started yet (not due) and months the contract is not effective.
            if (mStart <= today
                && stall.Contracts.Any(c => c.IsActive
                    && c.EffectivityDate <= mEnd && c.EffectivityDate.AddYears(c.DurationYears) >= mStart))
            {
                total += stall.MonthlyRate;
            }
            cursor = cursor.AddMonths(1);
        }
        return total;
    }

    /// <summary>
    /// Total monthly-rental obligation for occupied stalls over [start, end] — sum of each stall's
    /// MonthlyRate for every month its active contract is effective within the range. Used as the
    /// "expected" baseline for trend bar scaling so unpaid stalls (which have no record) still count.
    /// </summary>
    private static decimal CalculateMonthlyRentalObligation(IEnumerable<Stall> occupiedStalls, DateOnly start, DateOnly end)
    {
        if (end < start) return 0m;
        decimal total = 0m;
        foreach (var s in occupiedStalls)
        {
            var cursor = new DateOnly(start.Year, start.Month, 1);
            var last = new DateOnly(end.Year, end.Month, 1);
            while (cursor <= last)
            {
                var mStart = cursor;
                var mEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                if (s.Contracts.Any(c => c.IsActive
                        && c.EffectivityDate <= mEnd && c.EffectivityDate.AddYears(c.DurationYears) >= mStart))
                    total += s.MonthlyRate;
                cursor = cursor.AddMonths(1);
            }
        }
        return total;
    }

    /// <summary>
    /// Calculates growth percentage between current and previous period.
    /// Returns 0% if previous period has zero revenue or doesn't exist.
    /// </summary>
    private static decimal CalculateGrowthPercentage(decimal currentRevenue, decimal previousRevenue)
    {
        if (previousRevenue == 0)
            return 0m;

        return ((currentRevenue - previousRevenue) / previousRevenue) * 100m;
    }

    #endregion

}
