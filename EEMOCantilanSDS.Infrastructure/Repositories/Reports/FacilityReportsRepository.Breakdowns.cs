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

// Partial of FacilityReportsRepository: fee-type breakdown, daily-collection streak, and payment-distribution helpers.
public partial class FacilityReportsRepository
{
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
            .Where(pr => npmStallIds.Contains(pr.StallId))
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
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .Where(s => s.FacilityId == facilityId
                && s.Status == StallStatus.Active
               
                && s.Contracts.Any(c => c.IsActive))
            .ToListAsync(ct);

        var stallIds = stalls.Select(s => s.Id).ToList();

        // Amount paid per stall for this month (monthly record). ₱30 (daily fee) == 1 covered day.
        var monthlyPayments = await _context.PaymentRecords
            .AsNoTracking()
            .Where(p => stallIds.Contains(p.StallId) && p.BillingYear == monthStart.Year && p.BillingMonth == monthStart.Month)
            .ToListAsync(ct);
        // Per-stall coverage: rent paid ÷ ₱30 = covered days for that stall (whole month when fully paid),
        // plus explicit daily-collection dates. A monthly payment supersedes daily collections.
        var rentPaidByStall = monthlyPayments
            .Where(p => p.Status != PaymentStatus.Unpaid)
            .GroupBy(p => p.StallId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Status == PaymentStatus.Paid ? p.BaseRentalAmount : p.PartialAmount));

        var dailyDatesByStall = (await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId) && dc.CollectionDate >= monthStart && dc.CollectionDate <= monthEnd && dc.IsPaid)
                .Select(dc => new { dc.StallId, dc.CollectionDate })
                .ToListAsync(ct))
            .GroupBy(x => x.StallId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CollectionDate).ToHashSet());

        // Excused/absent dates per stall — a stall absent on a date is not expected to be collected,
        // so it drops out of that day's "should collect" set (and never counts as a missed day).
        var absentDatesByStall = (await _context.DailyCollections
                .AsNoTracking()
                .Where(dc => stallIds.Contains(dc.StallId) && dc.CollectionDate >= monthStart && dc.CollectionDate <= monthEnd && dc.IsAbsent)
                .Select(dc => new { dc.StallId, dc.CollectionDate })
                .ToListAsync(ct))
            .GroupBy(x => x.StallId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.CollectionDate).ToHashSet());

        bool AbsentOn(Stall s, DateOnly d) => absentDatesByStall.TryGetValue(s.Id, out var ds) && ds.Contains(d);

        bool CollectableOn(Stall s, DateOnly d) => s.Contracts.Any(c =>
            c.IsActive && c.EffectivityDate <= d && d <= c.EffectivityDate.AddYears(c.DurationYears));

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
        var absentDays = 0;
        var inScopeDays = 0;

        for (var date = monthStart; date <= monthEnd; date = date.AddDays(1))
        {
            var collectable = stalls.Where(s => CollectableOn(s, date)).ToList();
            string status;
            if (date < startDate || date > endDate || collectable.Count == 0)
            {
                status = "OutOfScope";
            }
            else if (collectable.All(s => AbsentOn(s, date)))
            {
                // Every payor under contract this day was excused/absent — the day is excused, not missed,
                // and is excluded from the coverage denominator.
                status = "Absent";
                absentDays++;
            }
            else
            {
                inScopeDays++;
                // Payors excused that day are dropped from the "should collect" set so an absence never
                // reads as a miss. A day is collected once any still-expected payor covered it.
                var expected = collectable.Where(s => !AbsentOn(s, date)).ToList();
                if (expected.Any(s => CoveredOn(s, date))) { status = "Collected"; collectedDays++; }
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
            if (!statusByDate.TryGetValue(date, out var st) || st == "OutOfScope" || st == "Absent") continue;
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
            CoverageRate: coverageRate,
            AbsentDays: absentDays
        );
    }

    #endregion

    #region Payment Distribution Helpers

    private static PaymentStatusDistributionDto BuildPaymentDistribution(IReadOnlyList<StallComplianceDto> stallCompliance)
    {
        var paid = stallCompliance.Count(s => s.Status.Equals("Paid", StringComparison.OrdinalIgnoreCase));
        var partial = stallCompliance.Count(s => s.Status.Equals("Partial", StringComparison.OrdinalIgnoreCase));
        var unpaid = stallCompliance.Count(s => s.Status.Equals("Unpaid", StringComparison.OrdinalIgnoreCase));

        // Excused/absent stalls are neither paid nor owing — they are excluded from the distribution.
        var total = paid + partial + unpaid;
        if (total == 0)
            return new PaymentStatusDistributionDto(0, 0m, 0, 0m, 0, 0m);

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
        // Count Unpaid explicitly so excused/absent stalls are not mistaken for unpaid.
        var unpaid = stallCompliance.Count(s => s.Status.Equals("Unpaid", StringComparison.OrdinalIgnoreCase));

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
               
                && s.Contracts.Any(c => c.IsActive))
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
            .Where(pr => activeStalls.Contains(pr.StallId))
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

}
