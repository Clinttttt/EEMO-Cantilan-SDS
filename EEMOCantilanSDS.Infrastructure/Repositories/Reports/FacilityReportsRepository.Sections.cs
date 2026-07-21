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

// Partial of FacilityReportsRepository: section-breakdown, top-stalls, and collection-performance helpers.
public partial class FacilityReportsRepository
{
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

        // Builds one section card from a set of stalls, using the SAME revenue/expected/rate/count logic for
        // canonical AND custom sections — so every card is computed identically and the cards reconcile to the
        // facility total. An empty set yields a ₱0 card (canonical sections always render; custom ones are only
        // built for groups that exist).
        async Task<SectionBreakdownDto> BuildCardAsync(string sectionName, List<Stall> stalls)
        {
            if (stalls.Count == 0)
                return new SectionBreakdownDto(sectionName, 0m, 0m, 0, 0, 0, 0);

            var stallIds = stalls.Select(s => s.Id).ToList();
            var stallsById = stalls.ToDictionary(s => s.Id);

            var allPaymentRecords = await _context.PaymentRecords
                .AsNoTracking()
                .Where(pr => stallIds.Contains(pr.StallId))
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
                    && !stallsWithMonthlyPayments.Contains(dc.StallId))
                .ToListAsync(ct);

            var dailyRevenue = dailyCollections.Sum(dc => stallsById.TryGetValue(dc.StallId, out var stall)
                && IsUnderContractOn(stall, dc.CollectionDate)
                    ? dc.DailyFee + (dc.FishKilos.HasValue ? dc.FishKilos.Value * _npmFishRate : 0m)
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
            var occupiedStalls = stalls.Where(s => s.Contracts.Any(c => c.IsActive)).ToList();
            var expectedRevenue = CalculateNpmExpectedDailyFeeRevenue(occupiedStalls, startDate, endDate);

            // Collection rate is for the daily stall-rent obligation only. Fish kilo fees are
            // revenue, but they must not inflate rent compliance or receivable-risk charts.
            // Numerator and denominator are both currently-billable (active) only — a closed stall's
            // collected money shows in Revenue but never in the rate, so the rate reflects current
            // billable performance and can never read above 100%.
            var rentCollected = dailyFeeRevenue + monthlyDailyFeeRevenue;
            var percentage = expectedRevenue > 0 ? Math.Min(100m, (rentCollected / expectedRevenue) * 100m) : 0m;
            var activeStalls = stalls.Count(s => CountNpmCollectableDays(s, startDate, endDate) > 0);
            var closedStalls = stalls.Count(s => s.Status == StallStatus.Closed);
            var noContractStalls = stalls.Count(s => s.Status == StallStatus.Active && !s.Contracts.Any(c => c.IsActive));

            return new SectionBreakdownDto(
                sectionName,
                actualRevenue,
                percentage,
                stalls.Count,
                activeStalls,
                closedStalls,
                noContractStalls
            );
        }

        // Canonical sections — always rendered (even at ₱0), so Cantilan's report is byte-for-byte unchanged.
        foreach (var section in sections)
        {
            var stalls = await _context.Stalls
                .AsNoTracking()
                .Where(s => s.FacilityId == facilityId && s.Section == section)
                .Include(s => s.Contracts.Where(c => c.IsActive))
                .ToListAsync(ct);

            breakdown.Add(await BuildCardAsync(SectionLabel(section), stalls));
        }

        // Per-LGU CUSTOM sections: NPM stalls with no canonical Section (Section == null) but a
        // CustomSectionName. Surface each distinct custom section as its own card — mirrors NCC's AreaNote
        // handling — so the section cards still reconcile to the facility total. Cantilan's NPM stalls all
        // carry a canonical Section, so nothing is added there.
        var customStalls = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.Section == null && s.CustomSectionName != null)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .ToListAsync(ct);

        foreach (var group in customStalls
            .GroupBy(s => s.CustomSectionName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            breakdown.Add(await BuildCardAsync(group.Key, group.ToList()));
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
                .Where(s => s.FacilityId == facilityId && s.AreaLocation == area)
                .Include(s => s.Contracts.Where(c => c.IsActive))
                .ToListAsync(ct);

            // Skip tiers with no stalls so the report never renders an empty ₱0 placeholder card
            // (e.g. an NCC that only uses Corner + Extension).
            if (stalls.Count == 0)
                continue;

            var stallIds = stalls.Select(s => s.Id).ToList();

            // Calculate actual revenue collected (only Paid/Partial)
            var allPaymentRecords = await _context.PaymentRecords
                .AsNoTracking()
                .Where(pr => stallIds.Contains(pr.StallId))
                .ToListAsync(ct);

            var actualRevenue = allPaymentRecords
                .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
                .Sum(pr => RecognizedRevenue(pr, includeFish: false));

            // Expected = rent obligation that has come due across the period (same due-month rule as
            // the headline collection-rate KPI), so the per-area rate matches the facility rate.
            var occupiedStalls = stalls.Where(s => s.Contracts.Any(c => c.IsActive)).ToList();
            var expectedRevenue = occupiedStalls.Sum(s => CalculateStallRentObligationDue(s, startDate, endDate));

            // Clamp to 100% like CalculateCollectionRateAsync so an overpayment can't read >100%.
            var percentage = expectedRevenue > 0 ? Math.Min(100m, (actualRevenue / expectedRevenue) * 100m) : 0m;
            var activeStalls = stalls.Count(s => s.Status == StallStatus.Active && s.Contracts.Any(c => c.IsActive));
            var closedStalls = stalls.Count(s => s.Status == StallStatus.Closed);
            var noContractStalls = stalls.Count(s => s.Status == StallStatus.Active && !s.Contracts.Any(c => c.IsActive));

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

        // NCC stalls with no standard tier (AreaLocation == null) still bill rent and belong in the totals.
        // Municipalities that use their OWN area names keep them in AreaNote — surface each distinct custom
        // name as its own card; stalls with no name at all fall into a single "No Location" card. Cantilan's
        // stalls carry the enum tier, so its report is unchanged.
        var unassigned = await _context.Stalls
            .AsNoTracking()
            .Where(s => s.FacilityId == facilityId && s.AreaLocation == null)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .ToListAsync(ct);

        // Local card builder — identical revenue/expected/counts logic as the tiers above.
        async Task AddCardAsync(string label, List<Domain.Entities.Facilities.Stall> groupStalls)
        {
            if (groupStalls.Count == 0) return;

            var stallIds = groupStalls.Select(s => s.Id).ToList();
            var allPaymentRecords = await _context.PaymentRecords
                .AsNoTracking()
                .Where(pr => stallIds.Contains(pr.StallId))
                .ToListAsync(ct);

            var actualRevenue = allPaymentRecords
                .Where(pr => IsPaymentInDateRange(pr.BillingYear, pr.BillingMonth, startDate, endDate))
                .Sum(pr => RecognizedRevenue(pr, includeFish: false));

            var occupiedStalls = groupStalls.Where(s => s.Contracts.Any(c => c.IsActive)).ToList();
            var expectedRevenue = occupiedStalls.Sum(s => CalculateStallRentObligationDue(s, startDate, endDate));
            var percentage = expectedRevenue > 0 ? Math.Min(100m, (actualRevenue / expectedRevenue) * 100m) : 0m;
            var activeStalls = groupStalls.Count(s => s.Status == StallStatus.Active && s.Contracts.Any(c => c.IsActive));
            var closedStalls = groupStalls.Count(s => s.Status == StallStatus.Closed);
            var noContractStalls = groupStalls.Count(s => s.Status == StallStatus.Active && !s.Contracts.Any(c => c.IsActive));

            breakdown.Add(new SectionBreakdownDto(
                label, actualRevenue, percentage, groupStalls.Count, activeStalls, closedStalls, noContractStalls));
        }

        // Distinct custom area names first (alphabetical, stable), then the catch-all "No Location".
        foreach (var group in unassigned
            .Where(s => !string.IsNullOrWhiteSpace(s.AreaNote))
            .GroupBy(s => s.AreaNote!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            await AddCardAsync(group.Key, group.ToList());
        }

        await AddCardAsync("No Location", unassigned.Where(s => string.IsNullOrWhiteSpace(s.AreaNote)).ToList());

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
            .Where(s => s.FacilityId == facilityId)
            .Include(s => s.Contracts.Where(c => c.IsActive))
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
            .Where(pr => stallIds.Contains(pr.StallId))
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
                   
                    && !stallsWithMonthlyPayments.Contains(dc.StallId))
                .ToListAsync(ct);

            dailyRevenueByStall = dailyCollections
                .Where(dc => stallsById.TryGetValue(dc.StallId, out var stall)
                    && IsUnderContractOn(stall, dc.CollectionDate))
                .GroupBy(dc => dc.StallId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(dc => dc.DailyFee
                        + (dc.FishKilos.HasValue ? dc.FishKilos.Value * _npmFishRate : 0m)));
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
               
                && s.Contracts.Any(c => c.IsActive))
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (activeStalls.Count == 0)
        {
            return new CollectionPerformanceDto(0, 0, 0);
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
        var stallsWithoutPayments = activeStalls.Count - stallsWithPayments;

        var fullyPaidCount = paymentStatuses.Values.Count(s => s == PaymentStatus.Paid);
        var partiallyPaidCount = paymentStatuses.Values.Count(s => s == PaymentStatus.Partial);
        var unpaidCount = paymentStatuses.Values.Count(s => s == PaymentStatus.Unpaid) + stallsWithoutPayments;

        return new CollectionPerformanceDto(fullyPaidCount, partiallyPaidCount, unpaidCount);
    }

    #endregion
}
