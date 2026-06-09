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

        var occupiedStalls = await CalculateOccupiedStallsAsync(facilityId, currentStart, currentEnd, ct);
        var totalStalls = await CalculateTotalStallsAsync(facilityId, ct);
        var stallCompliance = await GenerateStallComplianceAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        var pendingCount = stallCompliance.Count(s => s.Balance > 0m);
        var pendingAmount = stallCompliance.Sum(s => s.Balance);

  
        var revenueTrend = await GenerateRevenueTrendAsync(facilityCode, facilityId, period, year, month, weekNumber, ct);

        var paymentDistribution = BuildPaymentDistribution(stallCompliance);

        // Task 7.2-7.4: Generate section breakdown
        var sectionBreakdown = await GenerateSectionBreakdownAsync(facilityCode, facilityId, currentStart, currentEnd, ct);

        var topStalls = await IdentifyTopStallsAsync(facilityCode, facilityId, currentStart, currentEnd, ct);

        var collectionPerformance = BuildCollectionPerformance(stallCompliance);
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

    /// <summary>
    /// Collection history for a facility: every month of <paramref name="year"/> (up to the
    /// current month for the current year, all 12 for past years) plus a rolling 5-year summary.
    /// Each row reuses the same aggregation primitives as the per-period report, so the figures
    /// match exactly what the Weekly/Monthly/Yearly views show for those periods.
    /// </summary>
    public async Task<FacilityHistoryDto> GetFacilityHistoryAsync(
        FacilityCode facilityCode,
        int year,
        CancellationToken ct)
    {
        var facility = await _context.Facilities.AsNoTracking().FirstOrDefaultAsync(f => f.Code == facilityCode && !f.IsDeleted, ct)
            ?? throw new InvalidOperationException($"Facility with code {facilityCode} not found");
        var facilityId = facility.Id;
        var today = PhilippineTime.Today;

        // Current year: only months that have started. Past years: all 12. Future years: none.
        var maxMonth = year < today.Year ? 12 : year == today.Year ? today.Month : 0;

        var monthly = new List<FacilityHistoryPeriodDto>();
        for (var m = 1; m <= maxMonth; m++)
        {
            var (start, end) = CalculateMonthlyDateRange(year, m);
            var s = await ComputePeriodSummaryAsync(facilityCode, facilityId, start, end, ct);
            monthly.Add(new FacilityHistoryPeriodDto(
                new DateOnly(year, m, 1).ToString("MMMM"), year, m,
                s.TotalStalls, s.Collected, s.Outstanding, s.FollowUp, s.Rate));
        }

        var yearly = new List<FacilityHistoryPeriodDto>();
        for (var y = year - 4; y <= year; y++)
        {
            var (start, end) = CalculateYearlyDateRange(y);
            var s = await ComputePeriodSummaryAsync(facilityCode, facilityId, start, end, ct);
            yearly.Add(new FacilityHistoryPeriodDto(
                y.ToString(), y, null,
                s.TotalStalls, s.Collected, s.Outstanding, s.FollowUp, s.Rate));
        }

        return new FacilityHistoryDto(year, monthly, yearly);
    }

    /// <summary>
    /// Lean month snapshot for the dashboard — reuses the same canonical helpers as the full
    /// report (daily-aware NPM revenue, compliance-based paid/partial/unpaid, due-obligation rate)
    /// so the dashboard cards reconcile exactly with the facility page and reports, without the
    /// cost of building trends, sections, streaks, etc.
    /// </summary>
    public async Task<FacilitySnapshotDto> GetFacilitySnapshotAsync(
        FacilityCode facilityCode,
        int year,
        int month,
        CancellationToken ct)
    {
        var facility = await _context.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == facilityCode && !f.IsDeleted, ct);
        if (facility == null)
            return new FacilitySnapshotDto(0m, 0m, 0, 0, 0, 0, 0);

        var facilityId = facility.Id;
        var (start, end) = CalculateMonthlyDateRange(year, month);

        var collected = facilityCode == FacilityCode.NPM
            ? await CalculateNpmRevenueAsync(facilityId, start, end, ct)
            : await CalculateMonthlyRentalRevenueAsync(facilityId, start, end, ct);

        var compliance = await GenerateStallComplianceAsync(facilityCode, facilityId, start, end, ct);
        var perf = BuildCollectionPerformance(compliance);
        var pending = compliance.Sum(c => c.Balance);
        var rate = await CalculateCollectionRateAsync(facilityCode, facilityId, start, end, ct);
        var occupied = await CalculateOccupiedStallsAsync(facilityId, start, end, ct);

        return new FacilitySnapshotDto(
            collected,
            pending,
            perf.FullyPaidCount,
            perf.PartiallyPaidCount,
            perf.UnpaidCount,
            occupied,
            (int)Math.Round(rate));
    }

    /// <summary>
    /// Computes the five history figures for one period from the existing tested aggregation
    /// helpers, so they stay consistent with the main report. Outstanding/follow-up/stall-count
    /// come from the period-scoped compliance rows; collected and rate from their own helpers.
    /// </summary>
    private async Task<(int TotalStalls, decimal Collected, decimal Outstanding, int FollowUp, decimal Rate)>
        ComputePeriodSummaryAsync(FacilityCode facilityCode, Guid facilityId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var compliance = await GenerateStallComplianceAsync(facilityCode, facilityId, start, end, ct);
        var collected = facilityCode == FacilityCode.NPM
            ? await CalculateNpmRevenueAsync(facilityId, start, end, ct)
            : await CalculateMonthlyRentalRevenueAsync(facilityId, start, end, ct);
        var rate = await CalculateCollectionRateAsync(facilityCode, facilityId, start, end, ct);

        return (
            compliance.Count,
            collected,
            compliance.Sum(c => c.Balance),
            compliance.Count(c => c.Balance > 0m),
            rate);
    }

    #region Stall Compliance Helpers

    /// <summary>
    /// Per-stall compliance rows for the report page (powers both the delinquency table
    /// and the full "all stalls" table). Covers occupied stalls (active contract) only.
    /// Status/balance reflect the selected period; MissedMonths counts months in the
    /// period year (up to the period month) the stall was under contract yet paid nothing
    /// (NPM recognises daily collections; see <see cref="CountMissedMonths"/>).
    /// </summary>
    private async Task<IReadOnlyList<StallComplianceDto>> GenerateStallComplianceAsync(
        FacilityCode facilityCode,
        Guid facilityId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct)
    {
        var stalls = (await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => c.IsActive && !c.IsDeleted))
            .Where(s => s.FacilityId == facilityId
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
            .ToListAsync(ct))
            // Only stalls whose contract was actually effective during the selected period.
            // Without this, a stall whose contract starts after the period (or expired before it)
            // appears with a ₱0 obligation and renders as a phantom "Paid" payor for a month it
            // did not yet operate — e.g. viewing May when every contract begins June 5.
            .Where(s => CountNpmCollectableDays(s, startDate, endDate) > 0)
            .ToList();

        if (stalls.Count == 0)
            return Array.Empty<StallComplianceDto>();

        var stallIds = stalls.Select(s => s.Id).ToList();

        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => stallIds.Contains(pr.StallId) && !pr.IsDeleted)
            .ToListAsync(ct);

        var includeFish = facilityCode == FacilityCode.NPM;
        var (complianceStart, complianceEnd) = (startDate, endDate);

        var periodPayments = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, complianceStart, complianceEnd))
            .GroupBy(pr => pr.StallId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1)).ToList());

        var stallsWithNpmPeriodPayments = includeFish
            ? periodPayments
                .Where(kvp => kvp.Value.Any(pr => pr.Status != PaymentStatus.Unpaid))
                .Select(kvp => kvp.Key)
                .ToHashSet()
            : new HashSet<Guid>();

        var dailyByStall = includeFish
            ? await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId) && !dc.IsDeleted && dc.IsPaid
                    && !stallsWithNpmPeriodPayments.Contains(dc.StallId)
                    && dc.CollectionDate >= complianceStart && dc.CollectionDate <= complianceEnd)
                .GroupBy(dc => dc.StallId)
                .Select(g => new { StallId = g.Key, Total = g.Sum(dc => dc.DailyFee) })
                .ToDictionaryAsync(x => x.StallId, x => x.Total, ct)
            : new Dictionary<Guid, decimal>();

        // Months (this year, up to the report month) in which each NPM stall recorded at least one
        // paid daily collection. NPM is collected daily, so a daily collection — not a monthly
        // "Paid" PaymentRecord — is the real evidence that a month was paid. Used by CountMissedMonths.
        var yearStart = new DateOnly(endDate.Year, 1, 1);
        var dailyPaidMonthsByStall = includeFish
            ? (await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => stallIds.Contains(dc.StallId) && !dc.IsDeleted && dc.IsPaid
                        && dc.CollectionDate >= yearStart && dc.CollectionDate <= endDate)
                    .Select(dc => new { dc.StallId, dc.CollectionDate })
                    .ToListAsync(ct))
                .GroupBy(x => x.StallId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.CollectionDate.Month).ToHashSet())
            : new Dictionary<Guid, HashSet<int>>();

        var rows = new List<StallComplianceDto>();

        foreach (var s in stalls)
        {
            var contract = s.Contracts.FirstOrDefault(c => c.IsActive && !c.IsDeleted);

            decimal totalBill;
            string? orNumber = null;
            decimal amountPaid;

            // For NPM, the monthly record is the monthly equivalent of a daily ₱30 obligation.
            // The compliance balance is always selected-period obligation minus selected-period collections.
            if (includeFish)
            {
                var npmPayments = periodPayments.GetValueOrDefault(s.Id) ?? new List<PaymentRecord>();
                totalBill = CalculateNpmDailyObligation(s, complianceStart, complianceEnd)
                    + npmPayments.Sum(pr => CalculateNpmAdditionalCharges(pr, complianceStart, complianceEnd));
                amountPaid = npmPayments.Sum(pr => RecognizedNpmPaymentRevenue(pr, complianceStart, complianceEnd, s))
                    + dailyByStall.GetValueOrDefault(s.Id);
                orNumber = npmPayments
                    .Where(pr => !string.IsNullOrWhiteSpace(pr.ORNumber))
                    .OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1))
                    .Select(pr => pr.ORNumber)
                    .FirstOrDefault();
            }
            else if (periodPayments.TryGetValue(s.Id, out var payments) && payments.Count > 0)
            {
                // Monthly-billed facilities (TCC/NCC/BBQ/ICE): the bill is the FULL rent obligation
                // due across every month the contract is effective in the period (so unpaid months
                // without a record still count), plus any utilities actually billed on in-period
                // records. The balance then reconciles with MissedMonths and the Collection Rate.
                totalBill = CalculateStallRentObligationDue(s, complianceStart, complianceEnd)
                    + payments.Sum(pr => (pr.ElecAmount ?? 0) + (pr.WaterAmount ?? 0));
                amountPaid = payments.Sum(pr => pr.Status == PaymentStatus.Paid
                    ? pr.BaseRentalAmount + (pr.ElecAmount ?? 0) + (pr.WaterAmount ?? 0)
                    : pr.Status == PaymentStatus.Partial ? pr.PartialAmount : 0m);
                orNumber = payments
                    .Where(pr => !string.IsNullOrWhiteSpace(pr.ORNumber))
                    .OrderByDescending(pr => new DateTime(pr.BillingYear, pr.BillingMonth, 1))
                    .Select(pr => pr.ORNumber)
                    .FirstOrDefault();
            }
            else
            {
                // No payment record in the period. Monthly facilities still owe the full rent
                // obligation that has come due across the period (every effective, started month —
                // not just one). NPM has no monthly record here either, so its daily collections
                // (dailyByStall) settle against its own daily obligation.
                totalBill = includeFish
                    ? s.MonthlyRate
                    : CalculateStallRentObligationDue(s, complianceStart, complianceEnd);
                amountPaid = dailyByStall.GetValueOrDefault(s.Id);
            }

            var balance = Math.Max(0m, totalBill - amountPaid);
            var status = balance <= 0m ? "Paid" : amountPaid > 0m ? "Partial" : "Unpaid";

            var missedMonths = CountMissedMonths(
                paymentRecords, s, endDate, includeFish, dailyPaidMonthsByStall.GetValueOrDefault(s.Id));

            rows.Add(new StallComplianceDto(
                s.Id,
                s.StallNo,
                contract?.ActualOccupant ?? string.Empty,
                contract?.NameOnContract ?? string.Empty,
                s.Section.HasValue ? SectionLabel(s.Section) : s.AreaLocation?.ToString() ?? string.Empty,
                s.Type.ToString(),
                s.MonthlyRate,
                s.DailyRate ?? (includeFish ? FeeRates.NpmDailyFee : 0m),
                status,
                amountPaid,
                balance,
                orNumber,
                missedMonths,
                s.AreaSqm ?? 0,
                contract?.EffectivityDate,
                contract?.DurationYears ?? 0));
        }

        return rows.OrderBy(r => NaturalStallSortKey(r.StallNo), StringComparer.Ordinal).ToList();
    }

    // Orders stall numbers naturally so "2" precedes "10" (and "A2" precedes "A10"): each run of
    // digits is zero-padded to a fixed width before an ordinal compare. Plain string ordering put
    // "10" before "2" across the reports' All Stalls / Status Report tables.
    private static string NaturalStallSortKey(string stallNo) =>
        string.IsNullOrEmpty(stallNo)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(stallNo, "[0-9]+", m => m.Value.PadLeft(12, '0'));

    /// <summary>
    /// Counts months (this year, up to the report month) in which the stall was under an active
    /// contract yet recorded no payment at all — the delinquency signal for the report page.
    /// <para>
    /// Two rules keep this honest:
    /// (1) Months before the contract's effectivity (or after expiry) are never counted — a stall
    ///     cannot be "behind" on a month it was not yet operating.
    /// (2) For NPM, which is collected daily, a month counts as paid if it has either a paid daily
    ///     collection OR a non-Unpaid monthly record. Without this, every NPM stall would read as
    ///     fully delinquent because daily payors rarely have monthly "Paid" records.
    /// Other facilities keep the monthly-billing rule: a month is missed unless it has a fully-Paid
    /// record (Partial still counts as behind, matching the dashboard delinquency definition).
    /// </para>
    /// </summary>
    private static int CountMissedMonths(
        List<PaymentRecord> paymentRecords,
        Stall stall,
        DateOnly endDate,
        bool isNpm,
        HashSet<int>? dailyPaidMonths)
    {
        dailyPaidMonths ??= new HashSet<int>();

        var stallPayments = paymentRecords
            .Where(pr => pr.StallId == stall.Id && pr.BillingYear == endDate.Year)
            .ToList();

        // Months with a fully-Paid record (non-NPM "covered" rule).
        var paidMonths = stallPayments
            .Where(pr => pr.Status == PaymentStatus.Paid)
            .Select(pr => pr.BillingMonth)
            .ToHashSet();

        // Months with any non-Unpaid record (NPM "paid something" rule).
        var settledMonths = stallPayments
            .Where(pr => pr.Status != PaymentStatus.Unpaid)
            .Select(pr => pr.BillingMonth)
            .ToHashSet();

        var missed = 0;
        var today = PhilippineTime.Today;
        for (var m = 1; m <= endDate.Month; m++)
        {
            var monthStart = new DateOnly(endDate.Year, m, 1);
            var monthEnd = new DateOnly(endDate.Year, m, DateTime.DaysInMonth(endDate.Year, m));

            // Skip months that have not started yet (not yet due) — e.g. the Yearly view runs to
            // December, but Jul–Dec of the current year are not delinquent until they arrive.
            if (monthStart > today)
                continue;

            // Skip months the stall was not under an active contract (pre-effectivity / post-expiry).
            if (CountNpmCollectableDays(stall, monthStart, monthEnd) == 0)
                continue;

            var covered = isNpm
                ? settledMonths.Contains(m) || dailyPaidMonths.Contains(m)
                : paidMonths.Contains(m);

            if (!covered)
                missed++;
        }

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

    private static decimal CalculateNpmDailyObligation(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
            return 0m;

        return (endDate.DayNumber - startDate.DayNumber + 1) * FeeRates.NpmDailyFee;
    }

    private static bool IsContractCollectableOn(Contract contract, DateOnly date)
        => contract.IsActive
            && !contract.IsDeleted
            && contract.EffectivityDate <= date
            && date <= contract.ExpiryDate;

    private static bool IsStallCollectableOn(Stall stall, DateOnly date)
        => stall.Status == StallStatus.Active
            && !stall.IsDeleted
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
            .Include(s => s.Contracts.Where(c => c.IsActive && !c.IsDeleted))
            .Where(s => s.FacilityId == facilityId
                && s.Status == StallStatus.Active
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
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
            .Where(pr => npmStallIds.Contains(pr.StallId) && !pr.IsDeleted)
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
                && !dc.IsDeleted
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
            .Where(pr => pr.Stall!.FacilityId == facilityId && !pr.IsDeleted)
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
            .Include(s => s.Contracts.Where(c => c.IsActive && !c.IsDeleted))
            .Where(s => s.FacilityId == facilityId
                && s.Status == StallStatus.Active
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
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
                && stall.Contracts.Any(c => c.IsActive && !c.IsDeleted
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
                if (s.Contracts.Any(c => c.IsActive && !c.IsDeleted
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
            var npmCollectableStalls = await LoadNpmCollectableStallsAsync(facilityId, ct);
            var npmStallsById = npmCollectableStalls.ToDictionary(s => s.Id);
            var npmStallIds = npmStallsById.Keys.ToList();
            var expectedDailyFees = CalculateNpmExpectedDailyFeeRevenue(npmCollectableStalls, startDate, endDate);

            var npmPaymentRecords = await _context.PaymentRecords
                .AsNoTracking()
                .Where(pr => npmStallIds.Contains(pr.StallId) && !pr.IsDeleted)
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
                    && !dc.IsDeleted
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
            .Where(pr => occupiedStallIds.Contains(pr.StallId) && !pr.IsDeleted)
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
            .Include(s => s.Contracts.Where(c => c.IsActive && !c.IsDeleted))
            .Where(s => s.FacilityId == facilityId
                && !s.IsDeleted
                && s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
            .ToListAsync(ct);

        return stalls.Count(s => CountNpmCollectableDays(s, startDate, endDate) > 0);
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
        if (facilityCode == FacilityCode.NPM)
        {
            var compliance = await GenerateStallComplianceAsync(facilityCode, facilityId, startDate, endDate, ct);
            return (compliance.Count(s => s.Balance > 0m), compliance.Sum(s => s.Balance));
        }

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
        var npmCollectableStalls = facilityCode == FacilityCode.NPM
            ? await LoadNpmCollectableStallsAsync(facilityId, ct)
            : new List<Stall>();
        var npmStallsById = npmCollectableStalls.ToDictionary(s => s.Id);
        var npmStallIds = npmStallsById.Keys.ToList();
        var today = PhilippineTime.Today;

        // One data point per calendar day in this week's fixed day bucket
        // (week 1 = days 1-7, week 2 = 8-14, ...; a trailing week may be shorter).
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var dayLabel = date.ToString("ddd d"); // e.g. "Mon 8" — weekday + day-of-month (unambiguous across weeks)
            decimal revenue = 0m;
            decimal expectedRevenue = 0m;

            if (facilityCode == FacilityCode.NPM)
            {
                expectedRevenue = CalculateNpmExpectedDailyFeeRevenue(npmCollectableStalls, date, date);

                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => npmStallIds.Contains(pr.StallId)
                        && pr.BillingYear == date.Year
                        && pr.BillingMonth == date.Month
                        && !pr.IsDeleted)
                    .ToListAsync(ct);

                var monthlyRevenue = paymentRecords.Sum(pr => npmStallsById.TryGetValue(pr.StallId, out var stall)
                    ? RecognizedNpmPaymentRevenue(pr, date, date, stall)
                    : 0m);
                var stallsWithMonthlyPayments = paymentRecords
                    .Where(pr => pr.Status != PaymentStatus.Unpaid)
                    .Select(pr => pr.StallId)
                    .ToHashSet();

                var dailyCollections = await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => npmStallIds.Contains(dc.StallId)
                        && dc.CollectionDate == date
                        && dc.IsPaid
                        && !dc.IsDeleted
                        && !stallsWithMonthlyPayments.Contains(dc.StallId))
                    .ToListAsync(ct);

                var dailyRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                    && IsStallCollectableOn(stall, dc.CollectionDate)
                        ? dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m)
                        : 0m);

                revenue = monthlyRevenue + dailyRevenue;
            }

            trends.Add(new RevenueTrendDto(dayLabel, revenue, expectedRevenue, date == today));
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
        var npmCollectableStalls = facilityCode == FacilityCode.NPM
            ? await LoadNpmCollectableStallsAsync(facilityId, ct)
            : new List<Stall>();
        var npmStallsById = npmCollectableStalls.ToDictionary(s => s.Id);
        var npmStallIds = npmStallsById.Keys.ToList();
        var occupiedStalls = facilityCode == FacilityCode.NPM
            ? new List<Stall>()
            : await LoadOccupiedStallsAsync(facilityId, ct);
        var today = PhilippineTime.Today;

        // Generate 6 data points (last 6 months)
        for (int i = 5; i >= 0; i--)
        {
            var targetDate = new DateTime(year, month, 1).AddMonths(-i);
            var targetYear = targetDate.Year;
            var targetMonth = targetDate.Month;
            var monthLabel = targetDate.ToString("MMM yyyy"); // Jan 2024, Feb 2024, etc.

            decimal revenue = 0m;
            decimal expectedRevenue = 0m;

            if (facilityCode == FacilityCode.NPM)
            {
                // NPM: Include daily collections + monthly payments (only Paid/Partial)
                var (monthStart, monthEnd) = CalculateMonthlyDateRange(targetYear, targetMonth);
                expectedRevenue = CalculateNpmExpectedDailyFeeRevenue(npmCollectableStalls, monthStart, monthEnd);

                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => npmStallIds.Contains(pr.StallId)
                        && pr.BillingYear == targetYear
                        && pr.BillingMonth == targetMonth
                        && !pr.IsDeleted)
                    .ToListAsync(ct);

                var monthlyRevenue = paymentRecords.Sum(pr => npmStallsById.TryGetValue(pr.StallId, out var stall)
                    ? RecognizedNpmPaymentRevenue(pr, monthStart, monthEnd, stall)
                    : 0m);

                // Exclude stalls already counted via a monthly payment from the daily sum (no double-count).
                var stallsWithMonthlyPayments = paymentRecords
                    .Where(pr => pr.Status != PaymentStatus.Unpaid)
                    .Select(pr => pr.StallId)
                    .ToHashSet();

                var dailyCollections = await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => npmStallIds.Contains(dc.StallId)
                        && dc.CollectionDate >= monthStart
                        && dc.CollectionDate <= monthEnd
                        && dc.IsPaid
                        && !dc.IsDeleted
                        && !stallsWithMonthlyPayments.Contains(dc.StallId))
                    .ToListAsync(ct);

                var dailyRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                    && IsStallCollectableOn(stall, dc.CollectionDate)
                        ? dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m)
                        : 0m);

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
                // Expected = full monthly-rental obligation of occupied stalls (independent of
                // whether a record exists), so an unpaid stall still raises the bar's target.
                var (oblStart, oblEnd) = CalculateMonthlyDateRange(targetYear, targetMonth);
                expectedRevenue = CalculateMonthlyRentalObligation(occupiedStalls, oblStart, oblEnd);
            }

            trends.Add(new RevenueTrendDto(monthLabel, revenue, expectedRevenue, targetYear == today.Year && targetMonth == today.Month));
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
        var npmCollectableStalls = facilityCode == FacilityCode.NPM
            ? await LoadNpmCollectableStallsAsync(facilityId, ct)
            : new List<Stall>();
        var npmStallsById = npmCollectableStalls.ToDictionary(s => s.Id);
        var npmStallIds = npmStallsById.Keys.ToList();
        var occupiedStalls = facilityCode == FacilityCode.NPM
            ? new List<Stall>()
            : await LoadOccupiedStallsAsync(facilityId, ct);
        var today = PhilippineTime.Today;

        // Generate 5 data points (last 5 years)
        for (int i = 4; i >= 0; i--)
        {
            var targetYear = year - i;
            var yearLabel = targetYear.ToString(); // 2020, 2021, etc.

            decimal revenue = 0m;
            decimal expectedRevenue = 0m;

            if (facilityCode == FacilityCode.NPM)
            {
                // NPM: Include daily collections + monthly payments (only Paid/Partial)
                var (yearStart, yearEnd) = CalculateYearlyDateRange(targetYear);
                expectedRevenue = CalculateNpmExpectedDailyFeeRevenue(npmCollectableStalls, yearStart, yearEnd);

                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => npmStallIds.Contains(pr.StallId)
                        && pr.BillingYear == targetYear
                        && !pr.IsDeleted)
                    .ToListAsync(ct);

                var monthlyRevenue = paymentRecords.Sum(pr => npmStallsById.TryGetValue(pr.StallId, out var stall)
                    ? RecognizedNpmPaymentRevenue(pr, yearStart, yearEnd, stall)
                    : 0m);

                // Exclude stalls already counted via a monthly payment from the daily sum (no double-count).
                var stallsWithMonthlyPayments = paymentRecords
                    .Where(pr => pr.Status != PaymentStatus.Unpaid)
                    .Select(pr => pr.StallId)
                    .ToHashSet();

                var dailyCollections = await _context.DailyCollections
                    .AsNoTracking()
                    .Where(dc => npmStallIds.Contains(dc.StallId)
                        && dc.CollectionDate >= yearStart
                        && dc.CollectionDate <= yearEnd
                        && dc.IsPaid
                        && !dc.IsDeleted
                        && !stallsWithMonthlyPayments.Contains(dc.StallId))
                    .ToListAsync(ct);

                var dailyRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                    && IsStallCollectableOn(stall, dc.CollectionDate)
                        ? dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m)
                        : 0m);

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
                // Expected = full yearly rental obligation of occupied stalls, for proportional scaling.
                var (oblStart, oblEnd) = CalculateYearlyDateRange(targetYear);
                expectedRevenue = CalculateMonthlyRentalObligation(occupiedStalls, oblStart, oblEnd);
            }

            trends.Add(new RevenueTrendDto(yearLabel, revenue, expectedRevenue, targetYear == today.Year));
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

        var npmCollectableStalls = await LoadNpmCollectableStallsAsync(facilityId, ct);
        var npmStallsById = npmCollectableStalls.ToDictionary(s => s.Id);
        var npmStallIds = npmStallsById.Keys.ToList();

        var paymentRecords = await _context.PaymentRecords
            .AsNoTracking()
            .Where(pr => npmStallIds.Contains(pr.StallId) && !pr.IsDeleted)
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
            .Where(dc => npmStallIds.Contains(dc.StallId)
                && dc.CollectionDate >= startDate
                && dc.CollectionDate <= endDate
                && dc.IsPaid
                && !dc.IsDeleted
                && !stallsWithMonthlyPayments.Contains(dc.StallId))
            .ToListAsync(ct);

        var collectableDailyCollections = dailyCollections
            .Where(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                && IsStallCollectableOn(stall, dc.CollectionDate))
            .ToList();

        var dailyFeeFromCollections = collectableDailyCollections.Sum(dc => dc.DailyFee);
        var fishFeeFromCollections = collectableDailyCollections.Sum(dc => dc.FishKilos.HasValue
            ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo
            : 0m);

        // Daily fee from monthly payments (BaseRentalAmount = daily fee equivalent)
        var dailyFeeFromMonthly = periodPaymentRecords.Sum(pr => npmStallsById.TryGetValue(pr.StallId, out var stall)
            ? RecognizedNpmDailyFeeRevenue(pr, startDate, endDate, stall)
            : 0m);

        // Fish fee from monthly payments
        var fishFeeFromMonthly = periodPaymentRecords
            .Where(pr => pr.Status == PaymentStatus.Paid && IsWholeBillingMonthSelected(pr, startDate, endDate))
            .Sum(pr => pr.FishKilos.HasValue ? pr.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m);

        // Calculate fish kilo comparison (first half vs second half of period)
        var totalFishKilos = collectableDailyCollections.Sum(dc => dc.FishKilos ?? 0m)
            + periodPaymentRecords
                .Where(pr => pr.Status == PaymentStatus.Paid && IsWholeBillingMonthSelected(pr, startDate, endDate))
                .Sum(pr => pr.FishKilos ?? 0m);
        
        var fishComparison = CalculateFishKiloComparison(collectableDailyCollections, totalFishKilos, startDate, endDate);

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

    private static PaymentStatusDistributionDto BuildPaymentDistribution(IReadOnlyList<StallComplianceDto> stallCompliance)
    {
        var total = stallCompliance.Count;
        if (total == 0)
            return new PaymentStatusDistributionDto(0, 0m, 0, 0m, 0, 0m);

        var paid = stallCompliance.Count(s => s.Status.Equals("Paid", StringComparison.OrdinalIgnoreCase));
        var partial = stallCompliance.Count(s => s.Status.Equals("Partial", StringComparison.OrdinalIgnoreCase));
        var unpaid = total - paid - partial;

        return new PaymentStatusDistributionDto(
            paid,
            paid * 100m / total,
            partial,
            partial * 100m / total,
            unpaid,
            unpaid * 100m / total);
    }

    private static CollectionPerformanceDto BuildCollectionPerformance(IReadOnlyList<StallComplianceDto> stallCompliance)
    {
        var paid = stallCompliance.Count(s => s.Status.Equals("Paid", StringComparison.OrdinalIgnoreCase));
        var partial = stallCompliance.Count(s => s.Status.Equals("Partial", StringComparison.OrdinalIgnoreCase));
        var unpaid = stallCompliance.Count - paid - partial;

        return new CollectionPerformanceDto(paid, partial, unpaid);
    }

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
            var stallsById = stalls.ToDictionary(s => s.Id);

            // Get payment records for this section
            var allPaymentRecords = await _context.PaymentRecords
                .AsNoTracking()
                .Where(pr => stallIds.Contains(pr.StallId) && !pr.IsDeleted)
                .ToListAsync(ct);

            var monthlyRevenue = allPaymentRecords.Sum(pr => stallsById.TryGetValue(pr.StallId, out var stall)
                ? RecognizedNpmPaymentRevenue(pr, startDate, endDate, stall)
                : 0m);

            // Get stalls with monthly payments (exclude from daily count)
            var stallsWithMonthlyPayments = allPaymentRecords
                .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate)
                    && pr.Status != PaymentStatus.Unpaid)
                .Select(pr => pr.StallId)
                .ToHashSet();

            // Calculate daily revenue ONLY for stalls without monthly payments
            var dailyCollections = await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId)
                    && dc.CollectionDate >= startDate
                    && dc.CollectionDate <= endDate
                    && dc.IsPaid
                    && !dc.IsDeleted
                    && !stallsWithMonthlyPayments.Contains(dc.StallId))
                .ToListAsync(ct);

            var dailyRevenue = dailyCollections.Sum(dc => stallsById.TryGetValue(dc.StallId, out var stall)
                && IsStallCollectableOn(stall, dc.CollectionDate)
                    ? dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m)
                    : 0m);

            var dailyFeeRevenue = dailyCollections.Sum(dc => stallsById.TryGetValue(dc.StallId, out var stall)
                && IsStallCollectableOn(stall, dc.CollectionDate)
                    ? dc.DailyFee
                    : 0m);

            var monthlyDailyFeeRevenue = allPaymentRecords.Sum(pr => stallsById.TryGetValue(pr.StallId, out var stall)
                ? RecognizedNpmDailyFeeRevenue(pr, startDate, endDate, stall)
                : 0m);

            var actualRevenue = dailyRevenue + monthlyRevenue;

            // Calculate expected revenue for this section (occupied stalls only)
            var occupiedStalls = stalls.Where(s => s.Contracts.Any(c => c.IsActive && !c.IsDeleted)).ToList();
            var expectedRevenue = CalculateNpmExpectedDailyFeeRevenue(occupiedStalls, startDate, endDate);

            // Collection rate is for the daily stall-rent obligation only. Fish kilo fees are
            // revenue, but they must not inflate rent compliance or receivable-risk charts.
            var rentCollected = dailyFeeRevenue + monthlyDailyFeeRevenue;
            var percentage = expectedRevenue > 0 ? (rentCollected / expectedRevenue) * 100m : 0m;
            var sectionName = SectionLabel(section);
            var activeStalls = stalls.Count(s => CountNpmCollectableDays(s, startDate, endDate) > 0);
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
        // Include every NCC area tier. "Standard" was previously dropped, so any Standard-area
        // stall's revenue/counts never reconciled to the facility totals.
        var areas = new[] { NccAreaLocation.Corner, NccAreaLocation.Extension, NccAreaLocation.Standard };
        var breakdown = new List<SectionBreakdownDto>();

        // Calculate revenue and expected revenue for each area
        foreach (var area in areas)
        {
            var stalls = await _context.Stalls
                .AsNoTracking()
                .Where(s => s.FacilityId == facilityId && s.AreaLocation == area && !s.IsDeleted)
                .Include(s => s.Contracts.Where(c => c.IsActive && !c.IsDeleted))
                .ToListAsync(ct);

            // Skip tiers with no stalls so the report never renders an empty ₱0 placeholder card
            // (e.g. an NCC that only uses Corner + Extension).
            if (stalls.Count == 0)
                continue;

            var stallIds = stalls.Select(s => s.Id).ToList();

            // Calculate actual revenue collected (only Paid/Partial)
            var allPaymentRecords = await _context.PaymentRecords
                .AsNoTracking()
                .Where(pr => stallIds.Contains(pr.StallId) && !pr.IsDeleted)
                .ToListAsync(ct);

            var actualRevenue = allPaymentRecords
                .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
                .Sum(pr => RecognizedRevenue(pr, includeFish: false));

            // Expected = rent obligation that has come due across the period (same due-month rule as
            // the headline collection-rate KPI), so the per-area rate matches the facility rate.
            var occupiedStalls = stalls.Where(s => s.Contracts.Any(c => c.IsActive && !c.IsDeleted)).ToList();
            var expectedRevenue = occupiedStalls.Sum(s => CalculateStallRentObligationDue(s, startDate, endDate));

            // Clamp to 100% like CalculateCollectionRateAsync so an overpayment can't read >100%.
            var percentage = expectedRevenue > 0 ? Math.Min(100m, (actualRevenue / expectedRevenue) * 100m) : 0m;
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
        var stallsById = stalls.ToDictionary(s => s.Id);

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
                g => g.Sum(pr => facilityCode == FacilityCode.NPM
                    ? stallsById.TryGetValue(pr.StallId, out var stall)
                        ? RecognizedNpmPaymentRevenue(pr, startDate, endDate, stall)
                        : 0m
                    : RecognizedRevenue(pr, includeFish: false)));

        // Stalls already counted via a monthly payment must not also count daily collections (no double-count).
        var stallsWithMonthlyPayments = paymentRecords
            .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate)
                && pr.Status != PaymentStatus.Unpaid)
            .Select(pr => pr.StallId)
            .ToHashSet();

        // NPM also earns daily-collection revenue; aggregate it server-side in ONE query.
        Dictionary<Guid, decimal> dailyRevenueByStall;
        if (facilityCode == FacilityCode.NPM)
        {
            var dailyCollections = await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId)
                    && dc.CollectionDate >= startDate
                    && dc.CollectionDate <= endDate
                    && dc.IsPaid
                    && !dc.IsDeleted
                    && !stallsWithMonthlyPayments.Contains(dc.StallId))
                .ToListAsync(ct);

            dailyRevenueByStall = dailyCollections
                .Where(dc => stallsById.TryGetValue(dc.StallId, out var stall)
                    && IsStallCollectableOn(stall, dc.CollectionDate))
                .GroupBy(dc => dc.StallId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(dc => dc.DailyFee
                        + (dc.FishKilos.HasValue ? dc.FishKilos.Value * FeeRates.NpmFishFeePerKilo : 0m)));
        }
        else
        {
            dailyRevenueByStall = new Dictionary<Guid, decimal>();
        }

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
