using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class FacilityReportsRepository(AppDbContext context) : IFacilityReportsRepository
{
    private readonly AppDbContext _context = context;

    public async Task<FacilityReportsDto> GetFacilityReportsAsync(
        FacilityCode facilityCode,
        ReportPeriod period,
        int year,
        int? month,
        int? weekNumber,
        CancellationToken ct)
    {
        // Get facility ID
        var facility = await _context.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == facilityCode && !f.IsDeleted, ct);

        if (facility == null)
        {
            throw new InvalidOperationException($"Facility with code {facilityCode} not found");
        }

        var facilityId = facility.Id;

        // Calculate date ranges for current and previous periods
        var (currentStart, currentEnd) = CalculateDateRange(period, year, month, weekNumber);
        var (previousStart, previousEnd) = CalculatePreviousPeriodDateRange(period, year, month, weekNumber);

        // Task 5.2-5.6: Calculate summary metrics
        decimal currentRevenue;
        decimal previousRevenue;

        if (facilityCode == FacilityCode.NPM)
        {
            currentRevenue = await CalculateNpmRevenueAsync(facilityId, currentStart, currentEnd, ct);
            previousRevenue = await CalculateNpmRevenueAsync(facilityId, previousStart, previousEnd, ct);
        }
        else
        {
            currentRevenue = await CalculateMonthlyRentalRevenueAsync(facilityId, currentStart, currentEnd, ct);
            previousRevenue = await CalculateMonthlyRentalRevenueAsync(facilityId, previousStart, previousEnd, ct);
        }

        var revenueGrowth = CalculateGrowthPercentage(currentRevenue, previousRevenue);

        var currentCollectionRate = await CalculateCollectionRateAsync(facilityId, currentStart, currentEnd, ct);
        var previousCollectionRate = await CalculateCollectionRateAsync(facilityId, previousStart, previousEnd, ct);
        var collectionGrowth = CalculateGrowthPercentage(currentCollectionRate, previousCollectionRate);

        var occupiedStalls = await CalculateOccupiedStallsAsync(facilityId, ct);
        var (pendingCount, pendingAmount) = await CalculatePendingPaymentsAsync(facilityId, currentStart, currentEnd, ct);

        // Task 6.1-6.3: Generate revenue trend
        var revenueTrend = await GenerateRevenueTrendAsync(facilityCode, facilityId, period, year, month, weekNumber, ct);

        // Task 7.1: Calculate payment distribution
        var paymentDistribution = await CalculatePaymentDistributionAsync(facilityId, currentStart, currentEnd, ct);

        // Task 7.2-7.4: Generate section breakdown
        var sectionBreakdown = await GenerateSectionBreakdownAsync(facilityCode, facilityId, currentStart, currentEnd, ct);

        // Task 8.1: Identify top stalls
        var topStalls = await IdentifyTopStallsAsync(facilityCode, facilityId, currentStart, currentEnd, ct);

        // Task 8.2: Calculate collection performance
        var collectionPerformance = await CalculateCollectionPerformanceAsync(facilityId, currentStart, currentEnd, ct);

        // Task 9.1: Assemble final DTO
        return new FacilityReportsDto(
            TotalRevenue: currentRevenue,
            RevenueGrowthPercentage: revenueGrowth,
            CollectionRate: currentCollectionRate,
            CollectionGrowthPercentage: collectionGrowth,
            OccupiedStalls: occupiedStalls,
            PendingPaymentCount: pendingCount,
            PendingPaymentAmount: pendingAmount,
            RevenueTrend: revenueTrend,
            PaymentDistribution: paymentDistribution,
            SectionBreakdown: sectionBreakdown,
            TopStalls: topStalls,
            CollectionPerformance: collectionPerformance
        );
    }

    #region Date Range Calculation Helpers

    /// <summary>
    /// Calculates the start and end dates for the current period based on the report period type.
    /// </summary>
    /// <param name="period">The report period (Weekly, Monthly, Yearly)</param>
    /// <param name="year">The year</param>
    /// <param name="month">The month (required for Weekly and Monthly)</param>
    /// <param name="weekNumber">The week number (required for Weekly, 1-5)</param>
    /// <returns>Tuple of (startDate, endDate) as DateOnly</returns>
    private static (DateOnly startDate, DateOnly endDate) CalculateDateRange(
        ReportPeriod period,
        int year,
        int? month,
        int? weekNumber)
    {
        return period switch
        {
            ReportPeriod.Weekly => CalculateWeeklyDateRange(year, month!.Value, weekNumber!.Value),
            ReportPeriod.Monthly => CalculateMonthlyDateRange(year, month!.Value),
            ReportPeriod.Yearly => CalculateYearlyDateRange(year),
            _ => throw new ArgumentException($"Invalid report period: {period}")
        };
    }

    /// <summary>
    /// Calculates the start and end dates for the previous period (for growth comparison).
    /// </summary>
    /// <param name="period">The report period (Weekly, Monthly, Yearly)</param>
    /// <param name="year">The year</param>
    /// <param name="month">The month (required for Weekly and Monthly)</param>
    /// <param name="weekNumber">The week number (required for Weekly, 1-5)</param>
    /// <returns>Tuple of (startDate, endDate) as DateOnly</returns>
    private static (DateOnly startDate, DateOnly endDate) CalculatePreviousPeriodDateRange(
        ReportPeriod period,
        int year,
        int? month,
        int? weekNumber)
    {
        return period switch
        {
            ReportPeriod.Weekly => CalculatePreviousWeekDateRange(year, month!.Value, weekNumber!.Value),
            ReportPeriod.Monthly => CalculatePreviousMonthDateRange(year, month!.Value),
            ReportPeriod.Yearly => CalculatePreviousYearDateRange(year),
            _ => throw new ArgumentException($"Invalid report period: {period}")
        };
    }

    /// <summary>
    /// Calculates the date range for a specific week in a month.
    /// Week 1 = Days 1-7, Week 2 = Days 8-14, Week 3 = Days 15-21, Week 4 = Days 22-28, Week 5 = Days 29-31
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculateWeeklyDateRange(
        int year,
        int month,
        int weekNumber)
    {
        var startDay = (weekNumber - 1) * 7 + 1;
        var endDay = Math.Min(weekNumber * 7, DateTime.DaysInMonth(year, month));

        var startDate = new DateOnly(year, month, startDay);
        var endDate = new DateOnly(year, month, endDay);

        return (startDate, endDate);
    }

    /// <summary>
    /// Calculates the date range for a specific month.
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculateMonthlyDateRange(
        int year,
        int month)
    {
        var startDate = new DateOnly(year, month, 1);
        var endDate = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        return (startDate, endDate);
    }

    /// <summary>
    /// Calculates the date range for a specific year.
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculateYearlyDateRange(int year)
    {
        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        return (startDate, endDate);
    }

    /// <summary>
    /// Calculates the date range for the previous week.
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculatePreviousWeekDateRange(
        int year,
        int month,
        int weekNumber)
    {
        // If week 1, go to previous month's last week
        if (weekNumber == 1)
        {
            var previousMonth = month == 1 ? 12 : month - 1;
            var previousYear = month == 1 ? year - 1 : year;
            var daysInPreviousMonth = DateTime.DaysInMonth(previousYear, previousMonth);
            
            // Calculate the last week of previous month
            var lastWeekNumber = (daysInPreviousMonth + 6) / 7; // Ceiling division
            return CalculateWeeklyDateRange(previousYear, previousMonth, lastWeekNumber);
        }

        // Otherwise, just go to previous week in same month
        return CalculateWeeklyDateRange(year, month, weekNumber - 1);
    }

    /// <summary>
    /// Calculates the date range for the previous month.
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculatePreviousMonthDateRange(
        int year,
        int month)
    {
        var previousMonth = month == 1 ? 12 : month - 1;
        var previousYear = month == 1 ? year - 1 : year;

        return CalculateMonthlyDateRange(previousYear, previousMonth);
    }

    /// <summary>
    /// Calculates the date range for the previous year.
    /// </summary>
    private static (DateOnly startDate, DateOnly endDate) CalculatePreviousYearDateRange(int year)
    {
        return CalculateYearlyDateRange(year - 1);
    }

    #endregion

    #region Revenue Calculation Helpers

    /// <summary>
    /// Checks if a payment record falls within the date range based on billing year and month.
    /// </summary>
    private static bool IsPaymentInDateRange(int billingYear, int billingMonth, DateOnly startDate, DateOnly endDate)
    {
        // Create first day of billing month
        var billingStart = new DateOnly(billingYear, billingMonth, 1);
        
        // Check if billing month overlaps with date range
        return billingStart.Year >= startDate.Year && billingStart.Year <= endDate.Year &&
               billingStart.Month >= startDate.Month && billingStart.Month <= endDate.Month;
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
        // Sum daily collections (DailyFee + FishFeeAmount) where IsPaid = true
        var dailyRevenue = await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.Stall!.FacilityId == facilityId
                && dc.CollectionDate >= startDate
                && dc.CollectionDate <= endDate
                && dc.IsPaid
                && !dc.IsDeleted)
            .SumAsync(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0), ct);

        // Get all payment records and filter in memory
        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => pr.Stall!.FacilityId == facilityId && !pr.IsDeleted)
            .ToListAsync(ct);

        var monthlyRevenue = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .Sum(pr =>
            {
                // Only count revenue from Paid or Partial payments
                if (pr.Status == PaymentStatus.Paid)
                {
                    return pr.BaseRentalAmount
                        + (pr.ElecAmount ?? 0)
                        + (pr.WaterAmount ?? 0)
                        + (pr.FishKilos.HasValue ? pr.FishKilos.Value * 1.00m : 0);
                }
                else if (pr.Status == PaymentStatus.Partial)
                {
                    return pr.PartialAmount;
                }
                else
                {
                    return 0m; // Unpaid = no revenue
                }
            });

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
            .Where(pr => pr.Stall!.FacilityId == facilityId && !pr.IsDeleted)
            .ToListAsync(ct);

        return paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .Sum(pr =>
            {
                // Only count revenue from Paid or Partial payments
                if (pr.Status == PaymentStatus.Paid)
                {
                    return pr.BaseRentalAmount
                        + (pr.ElecAmount ?? 0)
                        + (pr.WaterAmount ?? 0);
                }
                else if (pr.Status == PaymentStatus.Partial)
                {
                    return pr.PartialAmount;
                }
                else
                {
                    return 0m; // Unpaid = no revenue
                }
            });
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

    #region Collection Rate Helpers

    /// <summary>
    /// Calculates collection rate as (amount collected / total billed) * 100.
    /// For NPM: Includes daily collections in the amount collected.
    /// Uses the stall's MonthlyRate property for expected bill calculation.
    /// </summary>
    private async Task<decimal> CalculateCollectionRateAsync(
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var facility = await _context.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == facilityId, ct);

        // Get all active stalls including their MonthlyRate
        var activeStalls = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && !s.IsDeleted)
            .Select(s => new { s.Id, s.MonthlyRate })
            .ToListAsync(ct);

        var activeStallIds = activeStalls.Select(s => s.Id).ToList();

        // Get all payment records and filter in memory
        var allPaymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => activeStallIds.Contains(pr.StallId) && !pr.IsDeleted)
            .ToListAsync(ct);

        var paymentRecords = allPaymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .Select(pr => new
            {
                StallId = pr.StallId,
                TotalBill = pr.BaseRentalAmount
                    + (pr.ElecAmount ?? 0)
                    + (pr.WaterAmount ?? 0)
                    + (pr.FishKilos.HasValue ? pr.FishKilos.Value * 1.00m : 0),
                AmountPaid = pr.Status == PaymentStatus.Paid
                    ? pr.BaseRentalAmount + (pr.ElecAmount ?? 0) + (pr.WaterAmount ?? 0) + (pr.FishKilos.HasValue ? pr.FishKilos.Value * 1.00m : 0)
                    : pr.Status == PaymentStatus.Partial
                        ? pr.PartialAmount
                        : 0
            })
            .ToList();

        decimal totalBilled = paymentRecords.Sum(pr => pr.TotalBill);
        decimal totalCollected = paymentRecords.Sum(pr => pr.AmountPaid);

        // For NPM: Add daily collections to total collected and calculate expected bills for stalls without payment records
        if (facility?.Code == FacilityCode.NPM)
        {
            var dailyCollections = await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => activeStallIds.Contains(dc.StallId)
                    && !dc.IsDeleted
                    && dc.IsPaid
                    && dc.CollectionDate >= startDate
                    && dc.CollectionDate <= endDate)
                .GroupBy(dc => dc.StallId)
                .Select(g => new
                {
                    StallId = g.Key,
                    TotalCollected = g.Sum(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0))
                })
                .ToListAsync(ct);

            // Add daily collections to total collected
            totalCollected += dailyCollections.Sum(dc => dc.TotalCollected);

            // For stalls with daily collections but no payment record, add expected monthly bill using stall's MonthlyRate
            var stallsWithPaymentRecords = paymentRecords.Select(pr => pr.StallId).ToHashSet();
            var stallsWithOnlyDailyCollections = dailyCollections
                .Where(dc => !stallsWithPaymentRecords.Contains(dc.StallId))
                .Select(dc => dc.StallId)
                .ToList();

            if (stallsWithOnlyDailyCollections.Any())
            {
                var stallMonthlyRates = activeStalls
                    .Where(s => stallsWithOnlyDailyCollections.Contains(s.Id))
                    .Sum(s => s.MonthlyRate);
                
                totalBilled += stallMonthlyRates;
            }
        }

        if (totalBilled == 0)
            return 0m;

        return (totalCollected / totalBilled) * 100m;
    }

    #endregion

    #region Occupancy Helpers

    /// <summary>
    /// Counts stalls with active contracts.
    /// </summary>
    private async Task<int> CalculateOccupiedStallsAsync(
        Guid facilityId,
        CancellationToken ct)
    {
        return await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
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
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var facility = await _context.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == facilityId, ct);

        // Get all OCCUPIED stalls (with active contracts) including their MonthlyRate
        var occupiedStalls = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId 
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
            .Select(s => new { s.Id, s.MonthlyRate })
            .ToListAsync(ct);

        if (occupiedStalls.Count == 0)
        {
            return (0, 0m);
        }

        // Get all payment records and filter in memory
        var allPaymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => occupiedStalls.Select(s => s.Id).Contains(pr.StallId) && !pr.IsDeleted)
            .ToListAsync(ct);

        var paymentsByStall = allPaymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .GroupBy(pr => pr.StallId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1)).First()
            );

        // Build pending payments list for ALL occupied stalls
        var pendingPayments = new List<dynamic>();

        foreach (var stall in occupiedStalls)
        {
            decimal totalBill = 0m;
            decimal amountPaid = 0m;

            // Check if stall has a payment record
            if (paymentsByStall.ContainsKey(stall.Id))
            {
                var pr = paymentsByStall[stall.Id];
                totalBill = pr.BaseRentalAmount
                    + (pr.ElecAmount ?? 0)
                    + (pr.WaterAmount ?? 0)
                    + (pr.FishKilos.HasValue ? pr.FishKilos.Value * 1.00m : 0);

                amountPaid = pr.Status == PaymentStatus.Paid
                    ? totalBill
                    : pr.Status == PaymentStatus.Partial
                        ? pr.PartialAmount
                        : 0;
            }
            else if (facility?.Code == FacilityCode.NPM)
            {
                // NPM stall with no payment record: use stall's MonthlyRate
                totalBill = stall.MonthlyRate;
                amountPaid = 0m;
            }
            else
            {
                // Non-NPM stall with no payment record: skip (no bill generated)
                continue;
            }

            pendingPayments.Add(new
            {
                StallId = stall.Id,
                TotalBill = totalBill,
                AmountPaid = amountPaid
            });
        }

        // For NPM: Add daily collections to AmountPaid
        if (facility?.Code == FacilityCode.NPM)
        {
            var dailyCollections = await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => occupiedStalls.Select(s => s.Id).Contains(dc.StallId)
                    && !dc.IsDeleted
                    && dc.IsPaid
                    && dc.CollectionDate >= startDate
                    && dc.CollectionDate <= endDate)
                .GroupBy(dc => dc.StallId)
                .Select(g => new
                {
                    StallId = g.Key,
                    TotalCollected = g.Sum(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0))
                })
                .ToListAsync(ct);

            var dailyCollectionsByStall = dailyCollections.ToDictionary(dc => dc.StallId, dc => dc.TotalCollected);

            // Update pending payments with daily collections
            pendingPayments = pendingPayments.Select(pp => new
            {
                pp.StallId,
                pp.TotalBill,
                AmountPaid = pp.AmountPaid + (dailyCollectionsByStall.ContainsKey(pp.StallId) ? dailyCollectionsByStall[pp.StallId] : 0m)
            }).ToList<dynamic>();
        }

        // Filter only Unpaid and Partial (exclude fully paid)
        var actualPending = pendingPayments
            .Where(pp => pp.AmountPaid < pp.TotalBill)
            .ToList();

        var count = actualPending.Count;
        var amount = actualPending.Sum(pp => (decimal)(pp.TotalBill - pp.AmountPaid));

        return (count, amount);
    }

    #endregion

    #region Trend Data Helpers

    /// <summary>
    /// Generates revenue trend data based on the report period.
    /// Weekly: 7 data points (Mon-Sun), Monthly: 6 data points (last 6 months), Yearly: 5 data points (last 5 years)
    /// </summary>
    private async Task<IReadOnlyList<RevenueTrendDto>> GenerateRevenueTrendAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        ReportPeriod period,
        int year,
        int? month,
        int? weekNumber,
        CancellationToken ct)
    {
        return period switch
        {
            ReportPeriod.Weekly => await GenerateWeeklyTrendAsync(facilityCode, facilityId, year, month!.Value, weekNumber!.Value, ct),
            ReportPeriod.Monthly => await GenerateMonthlyTrendAsync(facilityCode, facilityId, year, month!.Value, ct),
            ReportPeriod.Yearly => await GenerateYearlyTrendAsync(facilityCode, facilityId, year, ct),
            _ => throw new ArgumentException($"Invalid report period: {period}")
        };
    }

    private async Task<IReadOnlyList<RevenueTrendDto>> GenerateWeeklyTrendAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        int year,
        int month,
        int weekNumber,
        CancellationToken ct)
    {
        var (startDate, endDate) = CalculateWeeklyDateRange(year, month, weekNumber);
        var trends = new List<RevenueTrendDto>();

        // Generate 7 data points (Mon-Sun)
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var dayLabel = date.DayOfWeek.ToString().Substring(0, 3); // Mon, Tue, Wed, etc.
            decimal revenue = 0m;

            if (facilityCode == FacilityCode.NPM)
            {
                // NPM: Include daily collections
                revenue = await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => dc.Stall!.FacilityId == facilityId
                        && dc.CollectionDate == date
                        && dc.IsPaid
                        && !dc.IsDeleted)
                    .SumAsync(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0), ct);
            }

            trends.Add(new RevenueTrendDto(dayLabel, revenue));
        }

        return trends;
    }

    private async Task<IReadOnlyList<RevenueTrendDto>> GenerateMonthlyTrendAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        int year,
        int month,
        CancellationToken ct)
    {
        var trends = new List<RevenueTrendDto>();

        // Generate 6 data points (last 6 months)
        for (int i = 5; i >= 0; i--)
        {
            var targetDate = new DateTime(year, month, 1).AddMonths(-i);
            var targetYear = targetDate.Year;
            var targetMonth = targetDate.Month;
            var monthLabel = targetDate.ToString("MMM yyyy"); // Jan 2024, Feb 2024, etc.

            decimal revenue = 0m;

            if (facilityCode == FacilityCode.NPM)
            {
                // NPM: Include daily collections + monthly payments (only Paid/Partial)
                var (monthStart, monthEnd) = CalculateMonthlyDateRange(targetYear, targetMonth);
                
                var dailyRevenue = await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => dc.Stall!.FacilityId == facilityId
                        && dc.CollectionDate >= monthStart
                        && dc.CollectionDate <= monthEnd
                        && dc.IsPaid
                        && !dc.IsDeleted)
                    .SumAsync(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0), ct);

                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => pr.Stall!.FacilityId == facilityId
                        && pr.BillingYear == targetYear
                        && pr.BillingMonth == targetMonth
                        && !pr.IsDeleted)
                    .ToListAsync(ct);

                var monthlyRevenue = paymentRecords.Sum(pr =>
                {
                    // Only count revenue from Paid or Partial payments
                    if (pr.Status == PaymentStatus.Paid)
                    {
                        return pr.BaseRentalAmount
                            + (pr.ElecAmount ?? 0)
                            + (pr.WaterAmount ?? 0)
                            + (pr.FishKilos.HasValue ? pr.FishKilos.Value * 1.00m : 0);
                    }
                    else if (pr.Status == PaymentStatus.Partial)
                    {
                        return pr.PartialAmount;
                    }
                    else
                    {
                        return 0m; // Unpaid = no revenue
                    }
                });

                revenue = dailyRevenue + monthlyRevenue;
            }
            else
            {
                // Other facilities: Monthly payments only (only Paid/Partial)
                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => pr.Stall!.FacilityId == facilityId
                        && pr.BillingYear == targetYear
                        && pr.BillingMonth == targetMonth
                        && !pr.IsDeleted)
                    .ToListAsync(ct);

                revenue = paymentRecords.Sum(pr =>
                {
                    // Only count revenue from Paid or Partial payments
                    if (pr.Status == PaymentStatus.Paid)
                    {
                        return pr.BaseRentalAmount
                            + (pr.ElecAmount ?? 0)
                            + (pr.WaterAmount ?? 0);
                    }
                    else if (pr.Status == PaymentStatus.Partial)
                    {
                        return pr.PartialAmount;
                    }
                    else
                    {
                        return 0m; // Unpaid = no revenue
                    }
                });
            }

            trends.Add(new RevenueTrendDto(monthLabel, revenue));
        }

        return trends;
    }

    private async Task<IReadOnlyList<RevenueTrendDto>> GenerateYearlyTrendAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        int year,
        CancellationToken ct)
    {
        var trends = new List<RevenueTrendDto>();

        // Generate 5 data points (last 5 years)
        for (int i = 4; i >= 0; i--)
        {
            var targetYear = year - i;
            var yearLabel = targetYear.ToString(); // 2020, 2021, etc.

            decimal revenue = 0m;

            if (facilityCode == FacilityCode.NPM)
            {
                // NPM: Include daily collections + monthly payments (only Paid/Partial)
                var (yearStart, yearEnd) = CalculateYearlyDateRange(targetYear);
                
                var dailyRevenue = await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => dc.Stall!.FacilityId == facilityId
                        && dc.CollectionDate >= yearStart
                        && dc.CollectionDate <= yearEnd
                        && dc.IsPaid
                        && !dc.IsDeleted)
                    .SumAsync(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0), ct);

                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => pr.Stall!.FacilityId == facilityId
                        && pr.BillingYear == targetYear
                        && !pr.IsDeleted)
                    .ToListAsync(ct);

                var monthlyRevenue = paymentRecords.Sum(pr =>
                {
                    // Only count revenue from Paid or Partial payments
                    if (pr.Status == PaymentStatus.Paid)
                    {
                        return pr.BaseRentalAmount
                            + (pr.ElecAmount ?? 0)
                            + (pr.WaterAmount ?? 0)
                            + (pr.FishKilos.HasValue ? pr.FishKilos.Value * 1.00m : 0);
                    }
                    else if (pr.Status == PaymentStatus.Partial)
                    {
                        return pr.PartialAmount;
                    }
                    else
                    {
                        return 0m; // Unpaid = no revenue
                    }
                });

                revenue = dailyRevenue + monthlyRevenue;
            }
            else
            {
                // Other facilities: Monthly payments only (only Paid/Partial)
                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => pr.Stall!.FacilityId == facilityId
                        && pr.BillingYear == targetYear
                        && !pr.IsDeleted)
                    .ToListAsync(ct);

                revenue = paymentRecords.Sum(pr =>
                {
                    // Only count revenue from Paid or Partial payments
                    if (pr.Status == PaymentStatus.Paid)
                    {
                        return pr.BaseRentalAmount
                            + (pr.ElecAmount ?? 0)
                            + (pr.WaterAmount ?? 0);
                    }
                    else if (pr.Status == PaymentStatus.Partial)
                    {
                        return pr.PartialAmount;
                    }
                    else
                    {
                        return 0m; // Unpaid = no revenue
                    }
                });
            }

            trends.Add(new RevenueTrendDto(yearLabel, revenue));
        }

        return trends;
    }

    #endregion

    #region Payment Distribution Helpers

    /// <summary>
    /// Calculates payment status distribution (Paid, Partial, Unpaid counts and percentages).
    /// For NPM: Checks both PaymentRecords AND DailyCollections to determine if status should be Partial.
    /// </summary>
    private async Task<PaymentStatusDistributionDto> CalculatePaymentDistributionAsync(
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var facility = await _context.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == facilityId, ct);

        var activeStalls = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && !s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var totalStalls = activeStalls.Count;

        if (totalStalls == 0)
        {
            return new PaymentStatusDistributionDto(0, 0m, 0, 0m, 0, 0m);
        }

        // Get all payment records and filter in memory
        var allPaymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => activeStalls.Contains(pr.StallId) && !pr.IsDeleted)
            .ToListAsync(ct);

        var paymentStatuses = allPaymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .GroupBy(pr => pr.StallId)
            .Select(g => g.OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1)).First())
            .ToDictionary(pr => pr.StallId, pr => pr.Status);

        // For NPM: Check daily collections to determine if Unpaid should be Partial
        if (facility?.Code == FacilityCode.NPM)
        {
            var dailyCollections = await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => activeStalls.Contains(dc.StallId) 
                    && !dc.IsDeleted 
                    && dc.IsPaid
                    && dc.CollectionDate >= startDate 
                    && dc.CollectionDate <= endDate)
                .GroupBy(dc => dc.StallId)
                .Select(g => new { StallId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            // If a stall has daily collections but payment status is Unpaid, change to Partial
            foreach (var dc in dailyCollections)
            {
                if (paymentStatuses.ContainsKey(dc.StallId))
                {
                    // If status is Unpaid but has daily collections, mark as Partial
                    if (paymentStatuses[dc.StallId] == PaymentStatus.Unpaid && dc.Count > 0)
                    {
                        paymentStatuses[dc.StallId] = PaymentStatus.Partial;
                    }
                }
                else if (dc.Count > 0)
                {
                    // Stall has no payment record but has daily collections = Partial
                    paymentStatuses[dc.StallId] = PaymentStatus.Partial;
                }
            }
        }

        var stallsWithPayments = paymentStatuses.Count;
        var stallsWithoutPayments = totalStalls - stallsWithPayments;

        var paidCount = paymentStatuses.Values.Count(s => s == PaymentStatus.Paid);
        var partialCount = paymentStatuses.Values.Count(s => s == PaymentStatus.Partial);
        var unpaidCount = paymentStatuses.Values.Count(s => s == PaymentStatus.Unpaid) + stallsWithoutPayments;

        var paidPercentage = (decimal)paidCount / totalStalls * 100m;
        var partialPercentage = (decimal)partialCount / totalStalls * 100m;
        var unpaidPercentage = (decimal)unpaidCount / totalStalls * 100m;

        return new PaymentStatusDistributionDto(
            paidCount,
            paidPercentage,
            partialCount,
            partialPercentage,
            unpaidCount,
            unpaidPercentage
        );
    }

    #endregion

    #region Section Breakdown Helpers

    /// <summary>
    /// Generates section breakdown for NPM (VegetableArea, FishSection, MeatSection) and NCC (Corner, Extension).
    /// Returns empty list for other facilities.
    /// </summary>
    private async Task<IReadOnlyList<SectionBreakdownDto>> GenerateSectionBreakdownAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        if (facilityCode == FacilityCode.NPM)
        {
            return await GenerateNpmSectionBreakdownAsync(facilityId, startDate, endDate, ct);
        }
        else if (facilityCode == FacilityCode.NCC)
        {
            return await GenerateNccSectionBreakdownAsync(facilityId, startDate, endDate, ct);
        }

        return new List<SectionBreakdownDto>();
    }

    private async Task<IReadOnlyList<SectionBreakdownDto>> GenerateNpmSectionBreakdownAsync(
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var sections = new[] { MarketSection.VegetableArea, MarketSection.FishSection, MarketSection.MeatSection };
        var breakdown = new List<SectionBreakdownDto>();

        // Calculate revenue and expected revenue for each section
        foreach (var section in sections)
        {
            var stalls = await _context.Stalls
                .AsNoTracking()
                .Where(s => s.FacilityId == facilityId && s.Section == section && !s.IsDeleted)
                .Include(s => s.Contracts.Where(c => c.IsActive && !c.IsDeleted))
                .ToListAsync(ct);

            if (stalls.Count == 0)
            {
                breakdown.Add(new SectionBreakdownDto(section.ToString().Replace("Area", " Area"), 0m, 0m));
                continue;
            }

            var stallIds = stalls.Select(s => s.Id).ToList();

            // Calculate actual revenue collected
            var dailyRevenue = await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId)
                    && dc.CollectionDate >= startDate
                    && dc.CollectionDate <= endDate
                    && dc.IsPaid
                    && !dc.IsDeleted)
                .SumAsync(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0), ct);

            var allPaymentRecords = await _context.PaymentRecords
                .AsNoTracking()
                .Where(pr => stallIds.Contains(pr.StallId) && !pr.IsDeleted)
                .ToListAsync(ct);

            var monthlyRevenue = allPaymentRecords
                .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
                .Sum(pr =>
                {
                    if (pr.Status == PaymentStatus.Paid)
                    {
                        return pr.BaseRentalAmount
                            + (pr.ElecAmount ?? 0)
                            + (pr.WaterAmount ?? 0)
                            + (pr.FishKilos.HasValue ? pr.FishKilos.Value * 1.00m : 0);
                    }
                    else if (pr.Status == PaymentStatus.Partial)
                    {
                        return pr.PartialAmount;
                    }
                    else
                    {
                        return 0m;
                    }
                });

            var actualRevenue = dailyRevenue + monthlyRevenue;

            // Calculate expected revenue for this section (occupied stalls only)
            var occupiedStalls = stalls.Where(s => s.Contracts.Any(c => c.IsActive && !c.IsDeleted)).ToList();
            var expectedRevenue = occupiedStalls.Sum(s => s.MonthlyRate);

            // Calculate percentage as (actual / expected) * 100
            var percentage = expectedRevenue > 0 ? (actualRevenue / expectedRevenue) * 100m : 0m;
            var sectionName = section.ToString().Replace("Area", " Area");

            breakdown.Add(new SectionBreakdownDto(sectionName, actualRevenue, percentage));
        }

        return breakdown;
    }

    private async Task<IReadOnlyList<SectionBreakdownDto>> GenerateNccSectionBreakdownAsync(
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var areas = new[] { NccAreaLocation.Corner, NccAreaLocation.Extension };
        var breakdown = new List<SectionBreakdownDto>();

        // Calculate revenue and expected revenue for each area
        foreach (var area in areas)
        {
            var stalls = await _context.Stalls
                .AsNoTracking()
                .Where(s => s.FacilityId == facilityId && s.AreaLocation == area && !s.IsDeleted)
                .Include(s => s.Contracts.Where(c => c.IsActive && !c.IsDeleted))
                .ToListAsync(ct);

            if (stalls.Count == 0)
            {
                breakdown.Add(new SectionBreakdownDto(area.ToString(), 0m, 0m));
                continue;
            }

            var stallIds = stalls.Select(s => s.Id).ToList();

            // Calculate actual revenue collected (only Paid/Partial)
            var allPaymentRecords = await _context.PaymentRecords
                .AsNoTracking()
                .Where(pr => stallIds.Contains(pr.StallId) && !pr.IsDeleted)
                .ToListAsync(ct);

            var actualRevenue = allPaymentRecords
                .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
                .Sum(pr =>
                {
                    if (pr.Status == PaymentStatus.Paid)
                    {
                        return pr.BaseRentalAmount
                            + (pr.ElecAmount ?? 0)
                            + (pr.WaterAmount ?? 0);
                    }
                    else if (pr.Status == PaymentStatus.Partial)
                    {
                        return pr.PartialAmount;
                    }
                    else
                    {
                        return 0m;
                    }
                });

            // Calculate expected revenue for this area (occupied stalls only)
            var occupiedStalls = stalls.Where(s => s.Contracts.Any(c => c.IsActive && !c.IsDeleted)).ToList();
            var expectedRevenue = occupiedStalls.Sum(s => s.MonthlyRate);

            // Calculate percentage as (actual / expected) * 100
            var percentage = expectedRevenue > 0 ? (actualRevenue / expectedRevenue) * 100m : 0m;

            breakdown.Add(new SectionBreakdownDto(area.ToString(), actualRevenue, percentage));
        }

        return breakdown;
    }

    #endregion

    #region Top Stalls Helpers

    /// <summary>
    /// Identifies top 4 revenue-generating stalls for the period.
    /// </summary>
    private async Task<IReadOnlyList<TopStallDto>> IdentifyTopStallsAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var stalls = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && !s.IsDeleted)
            .Include(s => s.Contracts.Where(c => c.IsActive && !c.IsDeleted))
            .ToListAsync(ct);

        var stallRevenues = new List<(string StallNumber, string OccupantName, decimal Revenue)>();

        foreach (var stall in stalls)
        {
            decimal revenue = 0m;

            if (facilityCode == FacilityCode.NPM)
            {
                // NPM: Include daily collections + monthly payments (only Paid/Partial)
                var dailyRevenue = await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => dc.StallId == stall.Id
                        && dc.CollectionDate >= startDate
                        && dc.CollectionDate <= endDate
                        && dc.IsPaid
                        && !dc.IsDeleted)
                    .SumAsync(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0), ct);

                var allPaymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => pr.StallId == stall.Id && !pr.IsDeleted)
                    .ToListAsync(ct);

                var monthlyRevenue = allPaymentRecords
                    .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
                    .Sum(pr =>
                    {
                        // Only count revenue from Paid or Partial payments
                        if (pr.Status == PaymentStatus.Paid)
                        {
                            return pr.BaseRentalAmount
                                + (pr.ElecAmount ?? 0)
                                + (pr.WaterAmount ?? 0)
                                + (pr.FishKilos.HasValue ? pr.FishKilos.Value * 1.00m : 0);
                        }
                        else if (pr.Status == PaymentStatus.Partial)
                        {
                            return pr.PartialAmount;
                        }
                        else
                        {
                            return 0m; // Unpaid = no revenue
                        }
                    });

                revenue = dailyRevenue + monthlyRevenue;
            }
            else
            {
                // Other facilities: Monthly payments only (only Paid/Partial)
                var allPaymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => pr.StallId == stall.Id && !pr.IsDeleted)
                    .ToListAsync(ct);

                revenue = allPaymentRecords
                    .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
                    .Sum(pr =>
                    {
                        // Only count revenue from Paid or Partial payments
                        if (pr.Status == PaymentStatus.Paid)
                        {
                            return pr.BaseRentalAmount
                                + (pr.ElecAmount ?? 0)
                                + (pr.WaterAmount ?? 0);
                        }
                        else if (pr.Status == PaymentStatus.Partial)
                        {
                            return pr.PartialAmount;
                        }
                        else
                        {
                            return 0m; // Unpaid = no revenue
                        }
                    });
            }

            var occupantName = stall.Contracts.FirstOrDefault()?.ActualOccupant ?? "Vacant";
            stallRevenues.Add((stall.StallNo, occupantName, revenue));
        }

        return stallRevenues
            .OrderByDescending(sr => sr.Revenue)
            .Take(4)
            .Select(sr => new TopStallDto(sr.StallNumber, sr.OccupantName, sr.Revenue))
            .ToList();
    }

    #endregion

    #region Collection Performance Helpers

    /// <summary>
    /// Calculates collection performance (fully paid, partially paid, unpaid counts).
    /// For NPM: Checks both PaymentRecords AND DailyCollections to determine if status should be Partial.
    /// </summary>
    private async Task<CollectionPerformanceDto> CalculateCollectionPerformanceAsync(
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var facility = await _context.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == facilityId, ct);

        var activeStalls = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && !s.IsDeleted)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (activeStalls.Count == 0)
        {
            return new CollectionPerformanceDto(0, 0, 0);
        }

        // Get all payment records and filter in memory
        var allPaymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => activeStalls.Contains(pr.StallId) && !pr.IsDeleted)
            .ToListAsync(ct);

        var paymentStatuses = allPaymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .GroupBy(pr => pr.StallId)
            .Select(g => g.OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1)).First())
            .ToDictionary(pr => pr.StallId, pr => pr.Status);

        // For NPM: Check daily collections to determine if Unpaid should be Partial
        if (facility?.Code == FacilityCode.NPM)
        {
            var dailyCollections = await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => activeStalls.Contains(dc.StallId) 
                    && !dc.IsDeleted 
                    && dc.IsPaid
                    && dc.CollectionDate >= startDate 
                    && dc.CollectionDate <= endDate)
                .GroupBy(dc => dc.StallId)
                .Select(g => new { StallId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            // If a stall has daily collections but payment status is Unpaid, change to Partial
            foreach (var dc in dailyCollections)
            {
                if (paymentStatuses.ContainsKey(dc.StallId))
                {
                    // If status is Unpaid but has daily collections, mark as Partial
                    if (paymentStatuses[dc.StallId] == PaymentStatus.Unpaid && dc.Count > 0)
                    {
                        paymentStatuses[dc.StallId] = PaymentStatus.Partial;
                    }
                }
                else if (dc.Count > 0)
                {
                    // Stall has no payment record but has daily collections = Partial
                    paymentStatuses[dc.StallId] = PaymentStatus.Partial;
                }
            }
        }

        var stallsWithPayments = paymentStatuses.Count;
        var stallsWithoutPayments = activeStalls.Count - stallsWithPayments;

        var fullyPaidCount = paymentStatuses.Values.Count(s => s == PaymentStatus.Paid);
        var partiallyPaidCount = paymentStatuses.Values.Count(s => s == PaymentStatus.Partial);
        var unpaidCount = paymentStatuses.Values.Count(s => s == PaymentStatus.Unpaid) + stallsWithoutPayments;

        return new CollectionPerformanceDto(fullyPaidCount, partiallyPaidCount, unpaidCount);
    }

    #endregion
}
