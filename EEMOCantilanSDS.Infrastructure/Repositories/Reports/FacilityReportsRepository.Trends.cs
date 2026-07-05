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

// Partial of FacilityReportsRepository: revenue-trend and fish-kilo trend helpers.
public partial class FacilityReportsRepository
{
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
            ? await LoadNpmRevenueStallsAsync(facilityId, ct)
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
            decimal fishRevenue = 0m;

            if (facilityCode == FacilityCode.NPM)
            {
                expectedRevenue = CalculateNpmExpectedDailyFeeRevenue(npmCollectableStalls, date, date);

                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => npmStallIds.Contains(pr.StallId)
                        && pr.BillingYear == date.Year
                        && pr.BillingMonth == date.Month
                       )
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
                       
                        && !stallsWithMonthlyPayments.Contains(dc.StallId))
                    .ToListAsync(ct);

                var dailyRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                    && IsUnderContractOn(stall, dc.CollectionDate)
                        ? dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * _npmFishRate : 0m)
                        : 0m);

                // The fish-kilo (₱1/kg) portion only, so the trend bar can split rent vs fish.
                fishRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                    && IsUnderContractOn(stall, dc.CollectionDate)
                        ? (dc.FishKilos.HasValue ? dc.FishKilos.Value * _npmFishRate : 0m)
                        : 0m);

                revenue = monthlyRevenue + dailyRevenue;
            }

            trends.Add(new RevenueTrendDto(dayLabel, revenue, expectedRevenue, date == today, fishRevenue));
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
            ? await LoadNpmRevenueStallsAsync(facilityId, ct)
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
            decimal fishRevenue = 0m;

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
                       )
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
                       
                        && !stallsWithMonthlyPayments.Contains(dc.StallId))
                    .ToListAsync(ct);

                var dailyRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                    && IsUnderContractOn(stall, dc.CollectionDate)
                        ? dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * _npmFishRate : 0m)
                        : 0m);

                // The fish-kilo (₱1/kg) portion only, so the trend bar can split rent vs fish.
                fishRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                    && IsUnderContractOn(stall, dc.CollectionDate)
                        ? (dc.FishKilos.HasValue ? dc.FishKilos.Value * _npmFishRate : 0m)
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
                       )
                    .ToListAsync(ct);

                revenue = paymentRecords.Sum(pr => RecognizedRevenue(pr, includeFish: false));
                // Expected = full monthly-rental obligation of occupied stalls (independent of
                // whether a record exists), so an unpaid stall still raises the bar's target.
                var (oblStart, oblEnd) = CalculateMonthlyDateRange(targetYear, targetMonth);
                expectedRevenue = CalculateMonthlyRentalObligation(occupiedStalls, oblStart, oblEnd);
            }

            trends.Add(new RevenueTrendDto(monthLabel, revenue, expectedRevenue, targetYear == today.Year && targetMonth == today.Month, fishRevenue));
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
            ? await LoadNpmRevenueStallsAsync(facilityId, ct)
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
            decimal fishRevenue = 0m;

            if (facilityCode == FacilityCode.NPM)
            {
                // NPM: Include daily collections + monthly payments (only Paid/Partial)
                var (yearStart, yearEnd) = CalculateYearlyDateRange(targetYear);
                expectedRevenue = CalculateNpmExpectedDailyFeeRevenue(npmCollectableStalls, yearStart, yearEnd);

                var paymentRecords = await _context.PaymentRecords
                    .AsNoTracking()
                    .Where(pr => npmStallIds.Contains(pr.StallId)
                        && pr.BillingYear == targetYear
                       )
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
                       
                        && !stallsWithMonthlyPayments.Contains(dc.StallId))
                    .ToListAsync(ct);

                var dailyRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                    && IsUnderContractOn(stall, dc.CollectionDate)
                        ? dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * _npmFishRate : 0m)
                        : 0m);

                // The fish-kilo (₱1/kg) portion only, so the trend bar can split rent vs fish.
                fishRevenue = dailyCollections.Sum(dc => npmStallsById.TryGetValue(dc.StallId, out var stall)
                    && IsUnderContractOn(stall, dc.CollectionDate)
                        ? (dc.FishKilos.HasValue ? dc.FishKilos.Value * _npmFishRate : 0m)
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
                       )
                    .ToListAsync(ct);

                revenue = paymentRecords.Sum(pr => RecognizedRevenue(pr, includeFish: false));
                // Expected = full yearly rental obligation of occupied stalls, for proportional scaling.
                var (oblStart, oblEnd) = CalculateYearlyDateRange(targetYear);
                expectedRevenue = CalculateMonthlyRentalObligation(occupiedStalls, oblStart, oblEnd);
            }

            trends.Add(new RevenueTrendDto(yearLabel, revenue, expectedRevenue, targetYear == today.Year, fishRevenue));
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
               )
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

}
