using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of StallRepository: what the market is BEHIND on, for the collector's "Days still owed" screen.
//
// Its own read rather than more fields on the round. The round is loaded at every stall and must stay light; this walks every
// unsettled month of every payor and asks the office's settlement service to price each one, so a collector who is not chasing
// arrears never pays for it.
//
// The screen used to show one calendar month, so a day missed in August was invisible in September and the office had no way to
// collect it from the app at all. Reaching back is the point of this file.
public partial class StallRepository
{
    /// <summary>
    /// How far back a round will look for arrears.
    /// </summary>
    /// <remarks>
    /// Not a rule about what is owed - arrears do not expire, and the office's registers and reports still state every peso of
    /// them. It is a bound on ONE screen carried down a market on a phone: a list reaching back years would be unreadable at
    /// the stall and slow to build on a thin signal. Twelve months is the office's own reporting year.
    /// </remarks>
    private const int ArrearsLookbackMonths = 12;

    public async Task<MobileNpmArrearsDto> GetMobileNpmArrearsAsync(int year, int month, DateOnly collectionDate, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var effectiveEnd = GetEffectiveCollectionEnd(monthStart, monthEnd, collectionDate);

        // The earliest month a round will reach back to. Bounded by the window above, never before it.
        var lookbackStart = monthStart.AddMonths(-ArrearsLookbackMonths);

        var rateSnapshot = await _feeRateResolver.GetSnapshotAsync(ct);

        // The office's own name for each area, exactly as the round names it. A canonical section resolves to this tenant's
        // label; a custom section shows its per-stall name. The arrears screen groups by these words, so if it named an area
        // differently from the round the collector would be looking at two markets.
        var npmFacility = await _context.Facilities.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == FacilityCode.NPM, ct);

        string SectionDisplay(Stall s)
            => s.Section is { } sec
                ? (npmFacility?.SectionLabel(sec) ?? GetSectionName(sec))
                : (s.CustomSectionName ?? string.Empty);

        // Every space this market bills by the day that was held at some point in the window. A stall closed today may still owe
        // for months it traded in, so closure does not exclude it - and its own section being closed does not either: money owed
        // stays owed. What decides inclusion is whether a term of the stall covers any of the window.
        var stalls = await _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            .Include(s => s.DailyCollections)
            .Where(s => s.Facility!.Code == FacilityCode.NPM && !s.IsDeleted)
            .ToListAsync(ct);

        stalls = stalls
            .Where(s => s.Contracts.Any(c => c.OverlapsPeriod(lookbackStart, effectiveEnd)))
            .ToList();

        var closures = (await _context.NpmMarketClosures
            .AsNoTracking()
            .Where(c => c.ClosureDate >= lookbackStart && c.ClosureDate <= monthEnd)
            .Select(c => c.ClosureDate)
            .ToListAsync(ct))
            .ToHashSet();

        var payors = new List<MobileNpmStallArrearsDto>();

        foreach (var stall in stalls)
        {
            // Months that have CLOSED, oldest first, each priced as the office settles it.
            //
            // Never days times a fee computed here. Where a month is let for a rent it owes that rent whatever its calendar
            // gave it, and where the office bills pure days it owes its days: the settlement service asks the rule, so this
            // screen cannot state a figure the payor's own screen would contradict.
            var pastMonths = new List<MobileNpmMonthArrearDto>();

            for (var m = lookbackStart; m < monthStart; m = m.AddMonths(1))
            {
                if (!stall.Contracts.Any(c => c.OverlapsPeriod(m, m.AddMonths(1).AddDays(-1))))
                    continue;

                var payable = await NpmMonthSettlement.ComputePayableAsync(stall, m.Year, m.Month, ct);
                if (payable.Amount <= 0m)
                    continue;

                pastMonths.Add(new MobileNpmMonthArrearDto(m.Year, m.Month, payable.Days, payable.Amount));
            }

            // The month in progress, stated as its own days so the collector can say WHICH day money answers for.
            //
            // TODAY IS EXCLUDED. The daily round answers for today and every screen before this one already states it; a day in
            // hand is not an arrear, and listing it asked the collector to chase what he was standing there to collect.
            var daysThisMonth = UncollectedDays(stall, monthStart, effectiveEnd, closures)
                .Where(d => d < collectionDate)
                .ToList();

            var amountThisMonth = 0m;
            if (daysThisMonth.Count > 0)
            {
                // Priced by the same settlement, then trimmed to the days actually listed. Asking the service for the month and
                // scaling it would misstate a month whose last installments are folded into a month-end difference, so the days
                // are priced as the days they are - at each one's own resolved fee, which an office pricing its areas apart
                // needs, and never past what the month itself owes.
                var monthPayable = await NpmMonthSettlement.ComputePayableAsync(stall, year, month, ct);
                var listed = daysThisMonth.Sum(d => NpmDailyFee.ForStall(stall, rateSnapshot, d));

                amountThisMonth = Math.Min(listed, monthPayable.Amount);
            }

            if (pastMonths.Count == 0 && daysThisMonth.Count == 0)
                continue;

            var contract = stall.Contracts
                .Where(c => c.OverlapsPeriod(monthStart, effectiveEnd))
                .OrderByDescending(c => c.EffectivityDate)
                .FirstOrDefault()
                ?? stall.Contracts.OrderByDescending(c => c.EffectivityDate).FirstOrDefault();

            payors.Add(new MobileNpmStallArrearsDto(
                stall.Id,
                stall.StallNo,
                string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? "No active occupant" : contract.ActualOccupant,
                stall.Section,
                SectionDisplay(stall),
                NpmDailyFee.ForStall(stall, rateSnapshot, effectiveEnd),
                pastMonths,
                daysThisMonth,
                amountThisMonth));
        }

        return new MobileNpmArrearsDto(
            year,
            month,
            collectionDate,
            payors.Sum(p => p.TotalOutstanding),
            payors);
    }
}
