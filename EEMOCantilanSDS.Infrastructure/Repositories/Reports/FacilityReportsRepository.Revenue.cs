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
    private decimal RecognizedRevenue(PaymentRecord pr, bool includeFish) => pr.Status switch
    {
        PaymentStatus.Paid => pr.BaseRentalAmount
            + (pr.ElecAmount ?? 0)
            + (pr.WaterAmount ?? 0)
            + (includeFish && pr.FishKilos.HasValue ? pr.FishKilos.Value * _npmFishRate : 0m),
        PaymentStatus.Partial => pr.PartialAmount,
        _ => 0m
    };

    private static bool IsContractCollectableOn(Contract contract, DateOnly date)
        => contract.IsActive
            && contract.EffectivityDate <= date
            && date <= contract.ExpiryDate;

    private static bool IsStallCollectableOn(Stall stall, DateOnly date)
        => stall.Status == StallStatus.Active
            && stall.Contracts.Any(c => IsContractCollectableOn(c, date));

    // Revenue recognition (money ACTUALLY received) is independent of the stall's CURRENT status — it
    // only requires the stall to have been under an effective contract on that date. A closure is a
    // current-state flag and must never erase already-collected daily fees. Forward-looking
    // obligation/expected math keeps using the status-aware IsStallCollectableOn above.
    private static bool IsUnderContractOn(Stall stall, DateOnly date)
        => stall.Contracts.Any(c => IsContractCollectableOn(c, date));

    private static int CountNpmCollectableDays(Stall stall, DateOnly startDate, DateOnly endDate, IReadOnlySet<DateOnly>? absentDates = null)
    {
        if (endDate < startDate)
            return 0;

        var days = 0;
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            // Excused/absent days are not collectable — the payor owes nothing for a day they were
            // legitimately not operating, so they drop out of the obligation just like a pre-contract day.
            if (IsStallCollectableOn(stall, date) && (absentDates is null || !absentDates.Contains(date)))
                days++;
        }

        return days;
    }

    private decimal CalculateNpmDailyObligation(Stall stall, DateOnly startDate, DateOnly endDate, IReadOnlySet<DateOnly>? absentDates = null)
        => CountNpmCollectableDays(stall, startDate, endDate, absentDates) * stall.ResolveDailyFee(_npmDailyRate);

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

    // Stalls for REVENUE recognition — same as the collectable set but INCLUDING closed stalls, so a
    // closure never drops a stall's already-collected daily fees out of the Collected total. Closed
    // stalls still owe ₱0 going forward (the obligation/expected helpers remain status-aware).
    private async Task<List<Stall>> LoadNpmRevenueStallsAsync(Guid facilityId, CancellationToken ct)
    {
        return await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .Where(s => s.FacilityId == facilityId
                && s.Contracts.Any(c => c.IsActive))
            .ToListAsync(ct);
    }

    private decimal CalculateNpmExpectedDailyFeeRevenue(IEnumerable<Stall> stalls, DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            return 0m;

        return stalls.Sum(s => CalculateNpmDailyObligation(s, startDate, endDate));
    }

    private decimal CalculateNpmAdditionalCharges(PaymentRecord pr, DateOnly startDate, DateOnly endDate)
    {
        if (!IsWholeBillingMonthSelected(pr, startDate, endDate))
            return 0m;

        return pr.FishKilos.HasValue ? pr.FishKilos.Value * _npmFishRate : 0m;
    }

    private decimal RecognizedNpmPaymentRevenue(PaymentRecord pr, DateOnly startDate, DateOnly endDate, Stall? stall = null)
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
            + (pr.FishKilos.HasValue ? pr.FishKilos.Value * _npmFishRate : 0m);
    }

    private static bool IsWholeBillingMonthSelected(PaymentRecord pr, DateOnly startDate, DateOnly endDate)
    {
        var monthStart = new DateOnly(pr.BillingYear, pr.BillingMonth, 1);
        var monthEnd = new DateOnly(pr.BillingYear, pr.BillingMonth, DateTime.DaysInMonth(pr.BillingYear, pr.BillingMonth));
        return startDate <= monthStart && endDate >= monthEnd;
    }

    private decimal RecognizedNpmDailyFeeRevenue(PaymentRecord pr, DateOnly startDate, DateOnly endDate, Stall? stall = null)
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

    private decimal AllocatePrepaidDailyAmountToCollectableRange(
        decimal prepaidAmount,
        Stall stall,
        DateOnly monthStart,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        // A custom-section stall's prepaid daily amount is divided by ITS daily rate; canonical uses ordinance.
        var dailyRate = stall.ResolveDailyFee(_npmDailyRate);
        if (prepaidAmount <= 0m || dailyRate <= 0m || rangeEnd < rangeStart)
            return 0m;

        var monthEnd = new DateOnly(monthStart.Year, monthStart.Month, DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
        var collectableDays = new List<DateOnly>();
        for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
        {
            if (IsStallCollectableOn(stall, date))
                collectableDays.Add(date);
        }

        var fullCoveredDays = (int)Math.Floor(prepaidAmount / dailyRate);
        var remainder = prepaidAmount % dailyRate;
        var amount = collectableDays
            .Take(fullCoveredDays)
            .Where(d => d >= rangeStart && d <= rangeEnd)
            .Sum(_ => dailyRate);

        if (remainder > 0m && collectableDays.Count > fullCoveredDays)
        {
            var remainderDay = collectableDays[fullCoveredDays];
            if (remainderDay >= rangeStart && remainderDay <= rangeEnd)
                amount += remainder;
        }

        return amount;
    }

    private decimal AllocatePrepaidDailyAmountToRange(
        decimal prepaidAmount,
        DateOnly monthStart,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        if (prepaidAmount <= 0m || _npmDailyRate <= 0m || rangeEnd < rangeStart)
            return 0m;

        var fullCoveredDays = (int)Math.Floor(prepaidAmount / _npmDailyRate);
        var remainder = prepaidAmount % _npmDailyRate;
        var rangeStartIndex = rangeStart.DayNumber - monthStart.DayNumber;
        var rangeEndIndex = rangeEnd.DayNumber - monthStart.DayNumber;

        var firstFullIndex = 0;
        var lastFullIndex = fullCoveredDays - 1;
        var fullDaysInRange = lastFullIndex >= firstFullIndex
            ? Math.Max(0, Math.Min(rangeEndIndex, lastFullIndex) - Math.Max(rangeStartIndex, firstFullIndex) + 1)
            : 0;

        var amount = fullDaysInRange * _npmDailyRate;
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
        var npmCollectableStalls = await LoadNpmRevenueStallsAsync(facilityId, ct);
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
            && IsUnderContractOn(stall, dc.CollectionDate)
                ? dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * _npmFishRate : 0m)
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
    private static decimal CalculateStallRentObligationDue(Stall stall, DateOnly start, DateOnly end,
        IReadOnlySet<(int Year, int Month)>? excusedMonths = null)
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
            // Skip months that have not started yet (not due), months the contract is not effective,
            // and admin-excused months (₱0 owed for an approved closure).
            if (mStart <= today
                && (excusedMonths is null || !excusedMonths.Contains((cursor.Year, cursor.Month)))
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
    /// History-faithful monthly-rental obligation across rate changes: a month that already has a
    /// payment record is billed at that record's snapshot (<see cref="PaymentRecord.BaseRentalAmount"/>
    /// — the rate at the time it was recorded), and only months with NO record yet are billed at the
    /// stall's CURRENT <see cref="Stall.MonthlyRate"/>. This stops an admin rate change from
    /// retroactively re-pricing already-recorded months (which previously made paid-at-the-old-rate
    /// months show a phantom balance). Excused months, and unrecorded months that are not yet due or
    /// outside the active contract, owe ₱0.
    /// </summary>
    private static decimal CalculateMonthlyRentObligationDue(
        Stall stall, DateOnly start, DateOnly end,
        IReadOnlyList<PaymentRecord> records,
        IReadOnlySet<(int Year, int Month)>? excusedMonths = null)
    {
        if (end < start) return 0m;

        // Snapshot rent actually billed per recorded month (sum guards against an unexpected duplicate).
        var recordedByMonth = records
            .GroupBy(r => (r.BillingYear, r.BillingMonth))
            .ToDictionary(g => g.Key, g => g.Sum(r => r.BaseRentalAmount));

        var today = PhilippineTime.Today;
        decimal total = 0m;
        var cursor = new DateOnly(start.Year, start.Month, 1);
        var last = new DateOnly(end.Year, end.Month, 1);
        while (cursor <= last)
        {
            var key = (cursor.Year, cursor.Month);
            if (excusedMonths is null || !excusedMonths.Contains(key))
            {
                if (recordedByMonth.TryGetValue(key, out var snapshot))
                {
                    total += snapshot;   // bill exactly what was recorded (rate at that time)
                }
                else
                {
                    var mStart = cursor;
                    var mEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                    if (mStart <= today
                        && stall.Contracts.Any(c => c.IsActive
                            && c.EffectivityDate <= mEnd && c.EffectivityDate.AddYears(c.DurationYears) >= mStart))
                        total += stall.MonthlyRate;   // no record yet → current rate
                }
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
