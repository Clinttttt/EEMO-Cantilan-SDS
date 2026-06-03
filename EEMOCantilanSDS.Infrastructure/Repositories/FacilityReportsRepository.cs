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

        var currentCollectionRate = await CalculateCollectionRateAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        var previousCollectionRate = await CalculateCollectionRateAsync(facilityCode, facilityId, previousStart, previousEnd, ct);
        var collectionGrowth = CalculateGrowthPercentage(currentCollectionRate, previousCollectionRate);

        var occupiedStalls = await CalculateOccupiedStallsAsync(facilityId, ct);
        var totalStalls = await CalculateTotalStallsAsync(facilityId, ct);
        var stallCompliance = await GenerateStallComplianceAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        var pendingCount = stallCompliance.Count(s => s.Balance > 0m);
        var pendingAmount = stallCompliance.Sum(s => s.Balance);

  
        var revenueTrend = await GenerateRevenueTrendAsync(facilityCode, facilityId, period, year, month, weekNumber, ct);

        var paymentDistribution = await CalculatePaymentDistributionAsync(facilityCode, facilityId, currentStart, currentEnd, ct);

        // Task 7.2-7.4: Generate section breakdown
        var sectionBreakdown = await GenerateSectionBreakdownAsync(facilityCode, facilityId, currentStart, currentEnd, ct);

        var topStalls = await IdentifyTopStallsAsync(facilityCode, facilityId, currentStart, currentEnd, ct);

        var collectionPerformance = await CalculateCollectionPerformanceAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        var dailyCollectionStreak = await GenerateDailyCollectionStreakAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        var feeTypeBreakdown = await GenerateFeeTypeBreakdownAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        var fishKiloTrend = await GenerateFishKiloTrendAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        // Task 9.1: Assemble final DTO
        return new FacilityReportsDto(
            TotalRevenue: currentRevenue,
            RevenueGrowthPercentage: revenueGrowth,
            CollectionRate: currentCollectionRate,
            CollectionGrowthPercentage: collectionGrowth,
            OccupiedStalls: occupiedStalls,
            TotalStalls: totalStalls,
            PendingPaymentCount: pendingCount,
            PendingPaymentAmount: pendingAmount,
            RevenueTrend: revenueTrend,
            PaymentDistribution: paymentDistribution,
            SectionBreakdown: sectionBreakdown,
            TopStalls: topStalls,
            CollectionPerformance: collectionPerformance,
            DailyCollectionStreak: dailyCollectionStreak,
            FeeTypeBreakdown: feeTypeBreakdown,
            FishKiloTrend: fishKiloTrend,
            StallCompliance: stallCompliance
        );
    }

    #region Stall Compliance Helpers

    /// <summary>
    /// Per-stall compliance rows for the report page (powers both the delinquency table
    /// and the full "all stalls" table). Covers occupied stalls (active contract) only.
    /// Status/balance reflect the selected period; MissedMonths counts months in the
    /// period year (up to the period month) with no fully-Paid monthly record.
    /// </summary>
    private async Task<IReadOnlyList<StallComplianceDto>> GenerateStallComplianceAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var stalls = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => c.IsActive && !c.IsDeleted))
            .Where(s => s.FacilityId == facilityId
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
            .ToListAsync(ct);

        if (stalls.Count == 0)
            return Array.Empty<StallComplianceDto>();

        var stallIds = stalls.Select(s => s.Id).ToList();

        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => stallIds.Contains(pr.StallId) && !pr.IsDeleted)
            .ToListAsync(ct);

        var periodPayments = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .GroupBy(pr => pr.StallId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1)).First());

        var includeFish = facilityCode == FacilityCode.NPM;

        var dailyByStall = includeFish
            ? await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId) && !dc.IsDeleted && dc.IsPaid
                    && dc.CollectionDate >= startDate && dc.CollectionDate <= endDate)
                .GroupBy(dc => dc.StallId)
                .Select(g => new { StallId = g.Key, Total = g.Sum(dc => dc.DailyFee
                    + (dc.FishKilos.HasValue ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m)) })
                .ToDictionaryAsync(x => x.StallId, x => x.Total, ct)
            : new Dictionary<Guid, decimal>();

        var rows = new List<StallComplianceDto>();

        foreach (var s in stalls)
        {
            var contract = s.Contracts.FirstOrDefault(c => c.IsActive && !c.IsDeleted);

            decimal totalBill;
            string? orNumber = null;
            decimal amountPaid;

            // A monthly payment supersedes daily collections (no double-count, mirrors revenue calc).
            if (periodPayments.TryGetValue(s.Id, out var pr) && pr.Status != PaymentStatus.Unpaid)
            {
                totalBill = pr.BaseRentalAmount + (pr.ElecAmount ?? 0) + (pr.WaterAmount ?? 0)
                    + (includeFish && pr.FishKilos.HasValue ? pr.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m);
                amountPaid = pr.Status == PaymentStatus.Paid ? totalBill : pr.PartialAmount;
                orNumber = pr.ORNumber;
            }
            else
            {
                // No monthly payment → use NPM daily collections against the stall's monthly rate.
                totalBill = s.MonthlyRate;
                amountPaid = dailyByStall.GetValueOrDefault(s.Id);
                orNumber = pr?.ORNumber;
            }

            var balance = Math.Max(0m, totalBill - amountPaid);
            var status = balance <= 0m ? "Paid" : amountPaid > 0m ? "Partial" : "Unpaid";

            var missedMonths = CountMissedMonths(paymentRecords, s.Id, endDate);

            rows.Add(new StallComplianceDto(
                s.StallNo,
                contract?.ActualOccupant ?? string.Empty,
                contract?.NameOnContract ?? string.Empty,
                s.Section.HasValue ? SectionLabel(s.Section) : s.AreaLocation?.ToString() ?? string.Empty,
                s.MonthlyRate,
                s.DailyRate ?? (includeFish ? FeeRates.NpmDailyFee : 0m),
                status,
                amountPaid,
                balance,
                orNumber,
                missedMonths,
                s.AreaSqm ?? 0));
        }

        return rows.OrderBy(r => r.StallNo).ToList();
    }

    private static int CountMissedMonths(List<PaymentRecord> paymentRecords, Guid stallId, DateOnly endDate)
    {
        var paidMonths = paymentRecords
            .Where(pr => pr.StallId == stallId && pr.Status == PaymentStatus.Paid && pr.BillingYear == endDate.Year)
            .Select(pr => pr.BillingMonth)
            .ToHashSet();

        var missed = 0;
        for (var m = 1; m <= endDate.Month; m++)
            if (!paidMonths.Contains(m)) missed++;
        return missed;
    }

    private static string SectionLabel(MarketSection? section) => section switch
    {
        MarketSection.VegetableArea => "Vegetable Area",
        MarketSection.FishSection => "Fish Section",
        MarketSection.MeatSection => "Meat Section",
        _ => string.Empty
    };

    #endregion

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
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var startDay = (weekNumber - 1) * 7 + 1;
        var endDay = Math.Min(weekNumber * 7, daysInMonth);

        // A week beyond the month's days (e.g. week 5 of a 28-day February) has no days.
        // Return an empty range (start > end) so callers iterate nothing instead of throwing.
        if (startDay > daysInMonth)
        {
            var firstOfMonth = new DateOnly(year, month, 1);
            return (firstOfMonth, firstOfMonth.AddDays(-1));
        }

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
        // Get all payment records and filter in memory
        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => pr.Stall!.FacilityId == facilityId && !pr.IsDeleted)
            .ToListAsync(ct);

        var monthlyRevenue = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .Sum(pr => RecognizedRevenue(pr, includeFish: true));

        // Get stalls that have monthly payments in this period (exclude from daily count)
        var stallsWithMonthlyPayments = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate)
                && pr.Status != PaymentStatus.Unpaid)
            .Select(pr => pr.StallId)
            .ToHashSet();

        // Sum daily collections ONLY for stalls without monthly payments
        var dailyRevenue = await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.Stall!.FacilityId == facilityId
                && dc.CollectionDate >= startDate
                && dc.CollectionDate <= endDate
                && dc.IsPaid
                && !dc.IsDeleted
                && !stallsWithMonthlyPayments.Contains(dc.StallId))
            .SumAsync(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0), ct);

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
            .Sum(pr => RecognizedRevenue(pr, includeFish: false));
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
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
            .Select(s => new { s.Id, s.MonthlyRate })
            .ToListAsync(ct);

        if (occupiedStalls.Count == 0)
            return 0m;

        var occupiedStallIds = occupiedStalls.Select(s => s.Id).ToList();

        if (facilityCode == FacilityCode.NPM)
        {
            var daysInPeriod = endDate.DayNumber - startDate.DayNumber + 1;
            var expectedDailyFees = occupiedStalls.Count * FeeRates.NpmDailyFee * daysInPeriod;

            var dailyCollections = await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => occupiedStallIds.Contains(dc.StallId)
                    && !dc.IsDeleted
                    && dc.IsPaid
                    && dc.CollectionDate >= startDate
                    && dc.CollectionDate <= endDate)
                .ToListAsync(ct);

            var collectedDailyFees = dailyCollections.Sum(dc => dc.DailyFee);
            var collectedFishFees = dailyCollections.Sum(dc => dc.FishKilos.HasValue
                ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo
                : 0m);

            var totalAssessed = expectedDailyFees + collectedFishFees;
            var npmTotalCollected = collectedDailyFees + collectedFishFees;

            if (totalAssessed == 0)
                return 0m;

            return Math.Min(100m, (npmTotalCollected / totalAssessed) * 100m);
        }

        var allPaymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => occupiedStallIds.Contains(pr.StallId) && !pr.IsDeleted)
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

        var totalBilled = paymentRecords.Sum(pr => pr.TotalBill);
        decimal totalCollected = paymentRecords.Sum(pr => pr.AmountPaid);

        if (totalBilled == 0)
            return 0m;

        return Math.Min(100m, (totalCollected / totalBilled) * 100m);
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

    private async Task<int> CalculateTotalStallsAsync(
        Guid facilityId,
        CancellationToken ct)
    {
        return await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && !s.IsDeleted)
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

        // NPM also collects daily fees; aggregate them once, server-side.
        var dailyCollectionsByStall = facilityCode == FacilityCode.NPM
            ? await _context.DailyCollections
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
                    TotalCollected = g.Sum(dc => dc.DailyFee
                        + (dc.FishKilos.HasValue ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m))
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
                    + (pr.FishKilos.HasValue ? pr.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0);

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

        // One data point per calendar day in this week's fixed day bucket
        // (week 1 = days 1-7, week 2 = 8-14, ...; a trailing week may be shorter).
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var dayLabel = date.ToString("ddd d"); // e.g. "Mon 8" — weekday + day-of-month (unambiguous across weeks)
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

                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => pr.Stall!.FacilityId == facilityId
                        && pr.BillingYear == targetYear
                        && pr.BillingMonth == targetMonth
                        && !pr.IsDeleted)
                    .ToListAsync(ct);

                var monthlyRevenue = paymentRecords.Sum(pr => RecognizedRevenue(pr, includeFish: true));

                // Exclude stalls already counted via a monthly payment from the daily sum (no double-count).
                var stallsWithMonthlyPayments = paymentRecords
                    .Where(pr => pr.Status != PaymentStatus.Unpaid)
                    .Select(pr => pr.StallId)
                    .ToHashSet();

                var dailyRevenue = await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => dc.Stall!.FacilityId == facilityId
                        && dc.CollectionDate >= monthStart
                        && dc.CollectionDate <= monthEnd
                        && dc.IsPaid
                        && !dc.IsDeleted
                        && !stallsWithMonthlyPayments.Contains(dc.StallId))
                    .SumAsync(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0), ct);

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

                revenue = paymentRecords.Sum(pr => RecognizedRevenue(pr, includeFish: false));
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

                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => pr.Stall!.FacilityId == facilityId
                        && pr.BillingYear == targetYear
                        && !pr.IsDeleted)
                    .ToListAsync(ct);

                var monthlyRevenue = paymentRecords.Sum(pr => RecognizedRevenue(pr, includeFish: true));

                // Exclude stalls already counted via a monthly payment from the daily sum (no double-count).
                var stallsWithMonthlyPayments = paymentRecords
                    .Where(pr => pr.Status != PaymentStatus.Unpaid)
                    .Select(pr => pr.StallId)
                    .ToHashSet();

                var dailyRevenue = await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => dc.Stall!.FacilityId == facilityId
                        && dc.CollectionDate >= yearStart
                        && dc.CollectionDate <= yearEnd
                        && dc.IsPaid
                        && !dc.IsDeleted
                        && !stallsWithMonthlyPayments.Contains(dc.StallId))
                    .SumAsync(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0), ct);

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

                revenue = paymentRecords.Sum(pr => RecognizedRevenue(pr, includeFish: false));
            }

            trends.Add(new RevenueTrendDto(yearLabel, revenue));
        }

        return trends;
    }

    #endregion

    #region Fish Kilo Trend Helpers

    private async Task<IReadOnlyList<FishKiloTrendDto>> GenerateFishKiloTrendAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        if (facilityCode != FacilityCode.NPM)
        {
            return Array.Empty<FishKiloTrendDto>();
        }

        var dailyKilos = await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.Stall!.FacilityId == facilityId
                && dc.Stall.Section == MarketSection.FishSection
                && dc.CollectionDate >= startDate
                && dc.CollectionDate <= endDate
                && dc.IsPaid
                && !dc.IsDeleted)
            .GroupBy(dc => dc.CollectionDate)
            .Select(g => new
            {
                Date = g.Key,
                Kilos = g.Sum(dc => dc.FishKilos ?? 0m)
            })
            .ToListAsync(ct);

        var kiloByDate = dailyKilos.ToDictionary(k => k.Date, k => k.Kilos);
        var trend = new List<FishKiloTrendDto>();

        // Long spans (Yearly) aggregate by month to stay readable; shorter spans stay per-day.
        if (endDate.DayNumber - startDate.DayNumber > 45)
        {
            for (var m = new DateOnly(startDate.Year, startDate.Month, 1); m <= endDate; m = m.AddMonths(1))
            {
                var monthKilos = kiloByDate.Where(k => k.Key.Year == m.Year && k.Key.Month == m.Month).Sum(k => k.Value);
                trend.Add(new FishKiloTrendDto(m.ToString("MMM"), monthKilos));
            }
        }
        else
        {
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                trend.Add(new FishKiloTrendDto(date.ToString("MMM d"), kiloByDate.GetValueOrDefault(date)));
            }
        }

        return trend;
    }

    #endregion

    #region Fee Type Breakdown Helpers

    private async Task<FeeTypeBreakdownDto?> GenerateFeeTypeBreakdownAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        if (facilityCode != FacilityCode.NPM)
        {
            return null;
        }

        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => pr.Stall!.FacilityId == facilityId && !pr.IsDeleted)
            .ToListAsync(ct);

        var periodPaymentRecords = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .Where(pr => pr.Status is PaymentStatus.Paid or PaymentStatus.Partial)
            .ToList();

        // Get stalls with monthly payments (exclude from daily count)
        var stallsWithMonthlyPayments = periodPaymentRecords
            .Select(pr => pr.StallId)
            .ToHashSet();

        // Daily fee from actual daily collections (for stalls without monthly payments)
        var dailyCollections = await _context.DailyCollections
            .AsNoTracking()
            .Where(dc => dc.Stall!.FacilityId == facilityId
                && dc.CollectionDate >= startDate
                && dc.CollectionDate <= endDate
                && dc.IsPaid
                && !dc.IsDeleted
                && !stallsWithMonthlyPayments.Contains(dc.StallId))
            .ToListAsync(ct);

        var dailyFeeFromCollections = dailyCollections.Sum(dc => dc.DailyFee);
        var fishFeeFromCollections = dailyCollections.Sum(dc => dc.FishKilos.HasValue
            ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo
            : 0m);

        // Daily fee from monthly payments (BaseRentalAmount = daily fee equivalent)
        var dailyFeeFromMonthly = periodPaymentRecords.Sum(pr => pr.Status == PaymentStatus.Paid
            ? pr.BaseRentalAmount
            : Math.Min(pr.PartialAmount, pr.BaseRentalAmount));

        // Fish fee from monthly payments
        var fishFeeFromMonthly = periodPaymentRecords
            .Where(pr => pr.Status == PaymentStatus.Paid)
            .Sum(pr => pr.FishKilos.HasValue ? pr.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m);

        // Calculate fish kilo comparison (first half vs second half of period)
        var totalFishKilos = dailyCollections.Sum(dc => dc.FishKilos ?? 0m)
            + periodPaymentRecords.Where(pr => pr.Status == PaymentStatus.Paid).Sum(pr => pr.FishKilos ?? 0m);
        
        var fishComparison = CalculateFishKiloComparison(dailyCollections, totalFishKilos, startDate, endDate);

        return new FeeTypeBreakdownDto(
            DailyFeeAmount: dailyFeeFromCollections + dailyFeeFromMonthly,
            FishFeeAmount: fishFeeFromCollections + fishFeeFromMonthly,
            FishKiloComparison: fishComparison
        );
    }

    private static string CalculateFishKiloComparison(
        List<DailyCollection> dailyCollections,
        decimal totalFishKilos,
        DateOnly startDate,
        DateOnly endDate)
    {
        var daysDiff = endDate.DayNumber - startDate.DayNumber + 1;
        var periodName = daysDiff > 60 ? "year" : daysDiff > 10 ? "month" : "week";
        
        if (totalFishKilos == 0)
        {
            return $"No fish activity this {periodName}";
        }

        var midPoint = startDate.AddDays((endDate.DayNumber - startDate.DayNumber) / 2);
        var firstHalf = dailyCollections
            .Where(dc => dc.CollectionDate < midPoint)
            .Sum(dc => dc.FishKilos ?? 0m);
        var secondHalf = dailyCollections
            .Where(dc => dc.CollectionDate >= midPoint)
            .Sum(dc => dc.FishKilos ?? 0m);

        if (firstHalf == 0 && secondHalf > 0)
        {
            return $"Activity started mid-{periodName} • {secondHalf:N1} kg in second half";
        }

        if (firstHalf == 0)
        {
            return $"No fish activity this {periodName}";
        }

        var growth = ((secondHalf - firstHalf) / firstHalf) * 100;
        var trend = growth > 0 ? "↑" : growth < 0 ? "↓" : "→";
        var sign = growth > 0 ? "+" : "";

        return $"{trend} {sign}{growth:N0}% vs first half • {secondHalf - firstHalf:+0;-0;0} kg change";
    }

    #endregion

    #region Daily Collection Streak Helpers

    private async Task<DailyCollectionStreakDto?> GenerateDailyCollectionStreakAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        if (facilityCode != FacilityCode.NPM)
        {
            return null;
        }

        var monthStart = new DateOnly(startDate.Year, startDate.Month, 1);
        var monthEnd = new DateOnly(startDate.Year, startDate.Month, DateTime.DaysInMonth(startDate.Year, startDate.Month));
        var today = PhilippineTime.Today;
        var lastAuditableDay = today < monthStart
            ? monthStart.AddDays(-1)
            : today < monthEnd && today.Year == monthStart.Year && today.Month == monthStart.Month
                ? today
                : monthEnd;

        // Occupied stalls (active contract) — coverage is computed per stall.
        var stalls = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => c.IsActive && !c.IsDeleted))
            .Where(s => s.FacilityId == facilityId
                && s.Status == StallStatus.Active
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
            .ToListAsync(ct);

        var stallIds = stalls.Select(s => s.Id).ToList();

        // Amount paid per stall for this month (monthly record). ₱30 (daily fee) == 1 covered day.
        var monthlyPayments = await _context.PaymentRecords
            .AsNoTracking()
            .Where(p => stallIds.Contains(p.StallId) && p.BillingYear == monthStart.Year && p.BillingMonth == monthStart.Month && !p.IsDeleted)
            .ToListAsync(ct);
        // Per-stall coverage: rent paid ÷ ₱30 = covered days for that stall (whole month when fully paid),
        // plus explicit daily-collection dates. A monthly payment supersedes daily collections.
        var rentPaidByStall = monthlyPayments
            .Where(p => p.Status != PaymentStatus.Unpaid)
            .GroupBy(p => p.StallId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Status == PaymentStatus.Paid ? p.BaseRentalAmount : p.PartialAmount));

        var dailyDatesByStall = (await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId) && dc.CollectionDate >= monthStart && dc.CollectionDate <= monthEnd && dc.IsPaid && !dc.IsDeleted)
                .Select(dc => new { dc.StallId, dc.CollectionDate })
                .ToListAsync(ct))
            .GroupBy(x => x.StallId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CollectionDate).ToHashSet());

        bool CollectableOn(Stall s, DateOnly d) => s.Contracts.Any(c =>
            c.IsActive && !c.IsDeleted && c.EffectivityDate <= d && d <= c.EffectivityDate.AddYears(c.DurationYears));

        var collectableDaysByStall = stalls.ToDictionary(
            s => s.Id,
            s => Enumerable.Range(0, monthEnd.Day).Select(i => monthStart.AddDays(i)).Where(d => CollectableOn(s, d)).ToList());
        var prepaidDaysByStall = stalls.ToDictionary(
            s => s.Id,
            s => FeeRates.NpmDailyFee > 0 ? (int)Math.Floor(rentPaidByStall.GetValueOrDefault(s.Id) / FeeRates.NpmDailyFee) : 0);

        bool CoveredOn(Stall s, DateOnly d)
        {
            if (dailyDatesByStall.TryGetValue(s.Id, out var dts) && dts.Contains(d)) return true;
            var idx = collectableDaysByStall[s.Id].IndexOf(d);
            return idx >= 0 && idx < prepaidDaysByStall[s.Id];
        }

        var days = new List<DailyCollectionDayDto>();
        var statusByDate = new Dictionary<DateOnly, string>();
        var collectedDays = 0;
        var missedDays = 0;
        var inScopeDays = 0;

        for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
        {
            var collectable = stalls.Where(s => CollectableOn(s, date)).ToList();
            string status;
            if (date < startDate || date > endDate || collectable.Count == 0)
            {
                status = "OutOfScope";
            }
            else
            {
                inScopeDays++;
                // A day is collected once it is covered (a payor paid/prepaid that day). Unpaid payors are
                // surfaced via Payor Compliance + Delinquency Analytics, not by blanking the calendar.
                if (collectable.Any(s => CoveredOn(s, date))) { status = "Collected"; collectedDays++; }
                else if (date > lastAuditableDay) status = "Future";
                else { status = "Missed"; missedDays++; }
            }

            statusByDate[date] = status;
            days.Add(new DailyCollectionDayDto(date.Day, (int)date.DayOfWeek, status));
        }

        var streakEndDate = lastAuditableDay < endDate ? lastAuditableDay : endDate;
        var currentStreak = 0;
        for (var date = streakEndDate; date >= startDate && date >= monthStart; date = date.AddDays(-1))
        {
            if (!statusByDate.TryGetValue(date, out var st) || st == "OutOfScope") continue;
            if (st == "Collected") currentStreak++;
            else break;
        }

        var coverageRate = inScopeDays > 0 ? (int)Math.Round(collectedDays * 100.0 / inScopeDays) : 0;

        return new DailyCollectionStreakDto(
            MonthLabel: monthStart.ToString("MMMM yyyy"),
            CollectedDays: collectedDays,
            MissedDays: missedDays,
            CurrentStreakDays: currentStreak,
            Days: days,
            PartialDays: 0,
            CoverageRate: coverageRate
        );
    }

    #endregion

    #region Payment Distribution Helpers

    /// <summary>
    /// Calculates payment status distribution (Paid, Partial, Unpaid counts and percentages).
    /// For NPM: Checks both PaymentRecords AND DailyCollections to determine if status should be Partial.
    /// </summary>
    private async Task<PaymentStatusDistributionDto> CalculatePaymentDistributionAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var activeStalls = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
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
        if (facilityCode == FacilityCode.NPM)
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
                breakdown.Add(new SectionBreakdownDto(SectionLabel(section), 0m, 0m, 0, 0, 0, 0));
                continue;
            }

            var stallIds = stalls.Select(s => s.Id).ToList();

            // Get payment records for this section
            var allPaymentRecords = await _context.PaymentRecords
                .AsNoTracking()
                .Where(pr => stallIds.Contains(pr.StallId) && !pr.IsDeleted)
                .ToListAsync(ct);

            var monthlyRevenue = allPaymentRecords
                .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
                .Sum(pr => RecognizedRevenue(pr, includeFish: true));

            // Get stalls with monthly payments (exclude from daily count)
            var stallsWithMonthlyPayments = allPaymentRecords
                .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate)
                    && pr.Status != PaymentStatus.Unpaid)
                .Select(pr => pr.StallId)
                .ToHashSet();

            // Calculate daily revenue ONLY for stalls without monthly payments
            var dailyRevenue = await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId)
                    && dc.CollectionDate >= startDate
                    && dc.CollectionDate <= endDate
                    && dc.IsPaid
                    && !dc.IsDeleted
                    && !stallsWithMonthlyPayments.Contains(dc.StallId))
                .SumAsync(dc => dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * 1.00m : 0), ct);

            var actualRevenue = dailyRevenue + monthlyRevenue;

            // Calculate expected revenue for this section (occupied stalls only)
            var occupiedStalls = stalls.Where(s => s.Contracts.Any(c => c.IsActive && !c.IsDeleted)).ToList();
            var expectedRevenue = occupiedStalls.Sum(s => s.MonthlyRate);

            // Calculate percentage as (actual / expected) * 100
            var percentage = expectedRevenue > 0 ? (actualRevenue / expectedRevenue) * 100m : 0m;
            var sectionName = SectionLabel(section);
            var activeStalls = stalls.Count(s => s.Status == StallStatus.Active && s.Contracts.Any(c => c.IsActive && !c.IsDeleted));
            var closedStalls = stalls.Count(s => s.Status == StallStatus.Closed);
            var noContractStalls = stalls.Count(s => s.Status == StallStatus.Active && !s.Contracts.Any(c => c.IsActive && !c.IsDeleted));

            breakdown.Add(new SectionBreakdownDto(
                sectionName,
                actualRevenue,
                percentage,
                stalls.Count,
                activeStalls,
                closedStalls,
                noContractStalls
            ));
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
                breakdown.Add(new SectionBreakdownDto(area.ToString(), 0m, 0m, 0, 0, 0, 0));
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
                .Sum(pr => RecognizedRevenue(pr, includeFish: false));

            // Calculate expected revenue for this area (occupied stalls only)
            var occupiedStalls = stalls.Where(s => s.Contracts.Any(c => c.IsActive && !c.IsDeleted)).ToList();
            var expectedRevenue = occupiedStalls.Sum(s => s.MonthlyRate);

            // Calculate percentage as (actual / expected) * 100
            var percentage = expectedRevenue > 0 ? (actualRevenue / expectedRevenue) * 100m : 0m;
            var activeStalls = stalls.Count(s => s.Status == StallStatus.Active && s.Contracts.Any(c => c.IsActive && !c.IsDeleted));
            var closedStalls = stalls.Count(s => s.Status == StallStatus.Closed);
            var noContractStalls = stalls.Count(s => s.Status == StallStatus.Active && !s.Contracts.Any(c => c.IsActive && !c.IsDeleted));

            breakdown.Add(new SectionBreakdownDto(
                area.ToString(),
                actualRevenue,
                percentage,
                stalls.Count,
                activeStalls,
                closedStalls,
                noContractStalls
            ));
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

        if (stalls.Count == 0)
        {
            return Array.Empty<TopStallDto>();
        }

        var stallIds = stalls.Select(s => s.Id).ToList();

        // Batch-load every relevant payment record in ONE query, then group in memory
        // (IsPaymentInDateRange builds DateOnly values and cannot be translated to SQL).
        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => stallIds.Contains(pr.StallId) && !pr.IsDeleted)
            .ToListAsync(ct);

        var monthlyRevenueByStall = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
            .GroupBy(pr => pr.StallId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(pr => RecognizedRevenue(pr, includeFish: facilityCode == FacilityCode.NPM)));

        // Stalls already counted via a monthly payment must not also count daily collections (no double-count).
        var stallsWithMonthlyPayments = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate)
                && pr.Status != PaymentStatus.Unpaid)
            .Select(pr => pr.StallId)
            .ToHashSet();

        // NPM also earns daily-collection revenue; aggregate it server-side in ONE query.
        var dailyRevenueByStall = facilityCode == FacilityCode.NPM
            ? await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId)
                    && dc.CollectionDate >= startDate
                    && dc.CollectionDate <= endDate
                    && dc.IsPaid
                    && !dc.IsDeleted
                    && !stallsWithMonthlyPayments.Contains(dc.StallId))
                .GroupBy(dc => dc.StallId)
                .Select(g => new
                {
                    StallId = g.Key,
                    Revenue = g.Sum(dc => dc.DailyFee
                        + (dc.FishKilos.HasValue ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m))
                })
                .ToDictionaryAsync(x => x.StallId, x => x.Revenue, ct)
            : new Dictionary<Guid, decimal>();

        return stalls
            .Select(stall => new TopStallDto(
                stall.StallNo,
                stall.Contracts.FirstOrDefault()?.ActualOccupant ?? "Vacant",
                monthlyRevenueByStall.GetValueOrDefault(stall.Id) + dailyRevenueByStall.GetValueOrDefault(stall.Id)))
            .OrderByDescending(t => t.Revenue)
            .Take(4)
            .ToList();
    }

    #endregion

    #region Collection Performance Helpers

    /// <summary>
    /// Calculates collection performance (fully paid, partially paid, unpaid counts).
    /// For NPM: Checks both PaymentRecords AND DailyCollections to determine if status should be Partial.
    /// </summary>
    private async Task<CollectionPerformanceDto> CalculateCollectionPerformanceAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var activeStalls = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
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
        if (facilityCode == FacilityCode.NPM)
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
