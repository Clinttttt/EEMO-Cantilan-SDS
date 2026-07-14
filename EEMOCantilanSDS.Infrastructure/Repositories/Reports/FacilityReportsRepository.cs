using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Entry partial of FacilityReportsRepository: public IFacilityReportsRepository methods. Helpers live in sibling FacilityReportsRepository.*.cs partial files.
public partial class FacilityReportsRepository(AppDbContext context, IFeeRateResolver feeRateResolver) : IFacilityReportsRepository
{
    // Test/non-DI convenience: resolves fees from the context (empty rate table => ordinance constants).
    public FacilityReportsRepository(AppDbContext context) : this(context, new FeeRateResolver(context)) { }

    private readonly AppDbContext _context = context;
    private readonly IFeeRateResolver _feeRateResolver = feeRateResolver;

    // Current municipality's resolved NPM rates for the in-flight report. Initialized to the ordinance
    // constants so any code path resolves to Cantilan's numbers by default (byte-for-byte); each public
    // entry point refreshes them for its period via LoadNpmRatesAsync before the helpers run.
    private decimal _npmDailyRate = FeeRates.NpmDailyFee;
    private decimal _npmFishRate = FeeRates.NpmFishFeePerKilo;

    // Loads this tenant's NPM daily + fish rates as of the report period. With no data rows the snapshot
    // returns the ordinance constants, so Cantilan is unchanged; a second LGU gets its own rates.
    private async Task LoadNpmRatesAsync(DateOnly asOf, CancellationToken ct)
    {
        var snapshot = await _feeRateResolver.GetSnapshotAsync(ct);
        _npmDailyRate = snapshot.Resolve(FeeRateKey.NpmDailyStall, asOf);
        _npmFishRate = snapshot.Resolve(FeeRateKey.NpmFishPerKilo, asOf);
    }

    public async Task<int> GetEarliestActivityYearAsync(CancellationToken ct)
    {
        // Cheap MIN probes across the tenant-scoped sources that carry a period; the year picker just
        // needs the floor. No data yet → falls back to the current year (list becomes a single year).
        var years = new List<int> { PhilippineTime.Today.Year };

        var billing = await _context.PaymentRecords.AsNoTracking()
            .OrderBy(p => p.BillingYear).Select(p => (int?)p.BillingYear).FirstOrDefaultAsync(ct);
        if (billing is int by) years.Add(by);

        var daily = await _context.DailyCollections.AsNoTracking()
            .OrderBy(d => d.CollectionDate).Select(d => (DateOnly?)d.CollectionDate).FirstOrDefaultAsync(ct);
        if (daily is DateOnly dd) years.Add(dd.Year);

        var contract = await _context.Contracts.AsNoTracking()
            .OrderBy(c => c.EffectivityDate).Select(c => (DateOnly?)c.EffectivityDate).FirstOrDefaultAsync(ct);
        if (contract is DateOnly cd) years.Add(cd.Year);

        return years.Min();
    }

    public async Task<FacilityReportsDto> GetFacilityReportsAsync(
        FacilityCode facilityCode,
        ReportPeriod period,
        int year,
        int? month,
        int? weekNumber,
        CancellationToken ct)
    {
        // Get facility ID. A tenant may not operate every facility archetype (e.g. an LGU with no
        // NCC/BBQ/ICE). Rather than failing the whole multi-facility report, treat an absent facility
        // as one with no stalls/records: Guid.Empty matches no rows, so every helper below naturally
        // yields a zeroed section for it. Cantilan operates all facilities, so this fallback is never
        // taken there and its figures stay byte-for-byte identical.
        var facility = await _context.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == facilityCode, ct);

        var facilityId = facility?.Id ?? Guid.Empty;

        var (currentStart, currentEnd) = CalculateDateRange(period, year, month, weekNumber);
        var (previousStart, previousEnd) = CalculatePreviousPeriodDateRange(period, year, month, weekNumber);

        await LoadNpmRatesAsync(currentEnd, ct);

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
        var sectionBreakdown = await GenerateSectionBreakdownAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        var topStalls = await IdentifyTopStallsAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        var collectionPerformance = BuildCollectionPerformance(stallCompliance);
        var dailyCollectionStreak = await GenerateDailyCollectionStreakAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        var feeTypeBreakdown = await GenerateFeeTypeBreakdownAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
        var fishKiloTrend = await GenerateFishKiloTrendAsync(facilityCode, facilityId, currentStart, currentEnd, ct);
       
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
        var facility = await _context.Facilities.AsNoTracking().FirstOrDefaultAsync(f => f.Code == facilityCode, ct)
            ?? throw new InvalidOperationException($"Facility with code {facilityCode} not found");
        var facilityId = facility.Id;
        var today = PhilippineTime.Today;

        await LoadNpmRatesAsync(new DateOnly(year, 12, 31), ct);

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
        Guid facilityId,
        int year,
        int month,
        CancellationToken ct)
    {
        var (start, end) = CalculateMonthlyDateRange(year, month);

        await LoadNpmRatesAsync(end, ct);

        var collected = facilityCode == FacilityCode.NPM
            ? await CalculateNpmRevenueAsync(facilityId, start, end, ct)
            : await CalculateMonthlyRentalRevenueAsync(facilityId, start, end, ct);

        var compliance = await GenerateStallComplianceAsync(facilityCode, facilityId, start, end, ct);
        var perf = BuildCollectionPerformance(compliance);
        var pending = compliance.Sum(c => c.Balance);
        var rate = await CalculateCollectionRateAsync(facilityCode, facilityId, start, end, ct);
        var occupied = await CalculateOccupiedStallsAsync(facilityId, start, end, ct);

        // Paid transactions — NPM is per-day, so each ₱30 daily collection is one transaction (daily-fee
        // ÷ ₱30, same basis as the Financial Reports). Monthly facilities count paid + partial stalls.
        int paidTransactions;
        if (facilityCode == FacilityCode.NPM)
        {
            var breakdown = await GenerateFeeTypeBreakdownAsync(facilityCode, facilityId, start, end, ct);
            paidTransactions = (int)Math.Round((breakdown?.DailyFeeAmount ?? 0m) / _npmDailyRate);
        }
        else
        {
            paidTransactions = perf.FullyPaidCount + perf.PartiallyPaidCount;
        }

        return new FacilitySnapshotDto(
            collected,
            pending,
            perf.FullyPaidCount,
            perf.PartiallyPaidCount,
            perf.UnpaidCount,
            occupied,
            (int)Math.Round(rate),
            paidTransactions);
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

}
