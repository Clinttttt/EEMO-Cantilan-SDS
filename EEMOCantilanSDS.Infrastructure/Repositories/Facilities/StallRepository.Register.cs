using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Extensions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EEMOCantilanSDS.Infrastructure.Repositories;

// Partial of StallRepository: the List of Stallholders (IStallRegisterQueries) - the register the office prints and signs,
// stating each lessee, their space, the term and what a whole year of it comes to.
public partial class StallRepository
{
    public async Task<StallHoldersListDto> GetStallHoldersListAsync(FacilityCode facilityCode, MarketSection? section, string? searchTerm, CancellationToken ct)
    {
        var query = _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            // The stallholder roster lists CURRENT holders only — closed accounts are excluded entirely
            // (they still appear in the transaction/collection history for transparency, just not here).
            .Where(s => s.Facility!.Code == facilityCode && s.Status != StallStatus.Closed);

        if (section.HasValue)
            query = query.Where(s => s.Section == section.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(s =>
                s.StallNo.ToLower().Contains(term) ||
                s.Contracts.Any(c =>
                    c.ActualOccupant.ToLower().Contains(term) ||
                    (c.NameOnContract ?? "").ToLower().Contains(term)));
        }

        var stalls = await query
            .OrderBy(s => s.Section)
            .ThenBy(s => s.StallNo)
            .ToListAsync(ct);

        // Current holders only: also drop EXPIRED accounts — an (active) stall whose contract term has
        // already lapsed — as well as Closed (frozen) ones. Uses the same central rule (Stall.IsContractExpired)
        // as the closed-accounts register and the remove-inactive guard, so they can never diverge.
        // Expired/closed rows still appear in the transaction/collection history — just not on this roster.
        stalls = stalls.Where(s => s.Status != StallStatus.Closed && !s.IsContractExpired(_clock.PhilippineToday)).ToList();

        // ── The monetary columns must state what the stall is actually billed ──
        // A daily-collected facility (NPM) has no monthly contract rate. The official form's "Monthly
        // Rentals" column is the monthly EQUIVALENT of the ordinance daily fee (daily × 30), which is the
        // same figure the NPM report register prints. Previously this projection printed the stored
        // Stall.MonthlyRate — a number typed by whoever registered the stall — which only ever coincides
        // with the ordinance for a ₱30 municipality, so every other LGU was shown Cantilan's ₱900 next to
        // its own ₱40/day rate.
        //
        // The rate is resolved through Stall.ResolveDailyFee, the SAME rule billing and settlement use, so
        // a per-LGU CUSTOM section keeps its own rate and the roster can never disagree with the ledger.
        // Cantilan resolves to ₱30 × 30 = ₱900, exactly what it showed before.
        var isDailyBilled = facilityCode == FacilityCode.NPM;
        var rateSnapshot = isDailyBilled ? await _feeRateResolver.GetSnapshotAsync(ct) : null;
        var rateAsOf = DateOnly.FromDateTime(_clock.PhilippineNow);
        var npmDailyRate = isDailyBilled ? rateSnapshot!.Resolve(FeeRateKey.NpmDailyStall, rateAsOf) : 0m;
        var npmMonthlyRent = isDailyBilled ? rateSnapshot!.Resolve(FeeRateKey.NpmMonthlyStall, rateAsOf) : 0m;

        // The monthly rent the space is let for: the LGU's own stated market month when it has stated one, else
        // thirty of the fee THIS stall is billed at — its own rate in an area of the market's own, its area's rate
        // where the office prices areas apart, the market's rate otherwise. Cantilan resolves to ₱30 × 30 = ₱900,
        // exactly what it showed before.
        decimal MonthlyOf(Stall s) => isDailyBilled
            ? s.ResolveMonthlyRent(NpmDailyFee.ForStall(s, rateSnapshot!, rateAsOf), npmMonthlyRent)
            : s.MonthlyRate;

        // The tenant's own market-section display labels (e.g. "Gulayan") — resolved once. The MarketSection
        // enum stays the logical key; only the SHOWN label becomes tenant-aware, falling back to the canonical
        // name ("Vegetable Area"/…) when no custom label is set (so Cantilan is unchanged).
        var facility = await _context.Facilities.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == facilityCode, ct);

        // Group stalls by section (NPM has sections, others don't)
        var sectionsWithSection = stalls
            .Where(s => s.Section.HasValue)
            .GroupBy(s => s.Section!.Value)
            .Select(g => new StallHoldersSectionDto
            {
                SectionName = facility?.SectionLabel(g.Key) ?? GetSectionName(g.Key),
                StallCount = g.Count(),
                Rows = g.Select((s, idx) =>
                {
                    var contract = s.Contracts.FirstOrDefault(c => c.IsActive);
                    var durationYears = contract?.DurationYears ?? 0;
                    return new StallHolderRowDto
                    {
                        // The stall itself, so a caller can act on it rather than on its number: a number identifies a
                        // stall only within a facility and section, and the market has three spaces called "1".
                        StallId = s.Id,
                        RowNumber = idx + 1,
                        ActualOccupant = contract?.ActualOccupant ?? "",
                        NameOnContract = contract?.NameOnContract ?? "",
                        StallNo = s.StallNo,
                        EffectivityDate = contract?.EffectivityDate ?? default,
                        DurationYears = durationYears,
                        AreaSqm = s.AreaSqm,
                        MonthlyRentalRate = MonthlyOf(s),
                        ActualMonthlyRental = MonthlyOf(s),
                        WholeYearRental = MonthlyOf(s) * 12,
                        FishFeeTotal = null,   // List of Stallholders is base rental only — no fish/elec/water
                        IsClosed = s.Status == StallStatus.Closed,
                        // Space-only and extension rows print "No contract" with the contract columns left blank.
                        Arrangement = contract?.Arrangement ?? OccupancyArrangement.SignedContract
                    };
                }).ToList(),
                SectionMonthlyTotal = g.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionActualMonthly = g.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionWholeYearTotal = g.Where(s => s.Status == StallStatus.Active).Sum(s => MonthlyOf(s) * 12),
                SectionFishFeeTotal = 0   // base rental only — additional fees (fish/elec/water) are not part of this list
            }).ToList();

        // NPM per-LGU CUSTOM sections: Section is null but a CustomSectionName is set. Group each custom
        // section into its own named group (mirrors the canonical grouping) so the roster shows and counts
        // them, instead of lumping them into "All Stalls".
        foreach (var group in stalls
            .Where(s => !s.Section.HasValue && !string.IsNullOrWhiteSpace(s.CustomSectionName))
            .GroupBy(s => s.CustomSectionName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var groupStalls = group.ToList();
            sectionsWithSection.Add(new StallHoldersSectionDto
            {
                SectionName = group.Key,
                StallCount = groupStalls.Count,
                Rows = groupStalls.Select((s, idx) =>
                {
                    var contract = s.Contracts.FirstOrDefault(c => c.IsActive);
                    return new StallHolderRowDto
                    {
                        // The stall itself, so a caller can act on it rather than on its number: a number identifies a
                        // stall only within a facility and section, and the market has three spaces called "1".
                        StallId = s.Id,
                        RowNumber = idx + 1,
                        ActualOccupant = contract?.ActualOccupant ?? "",
                        NameOnContract = contract?.NameOnContract ?? "",
                        StallNo = s.StallNo,
                        EffectivityDate = contract?.EffectivityDate ?? default,
                        DurationYears = contract?.DurationYears ?? 0,
                        AreaSqm = s.AreaSqm,
                        MonthlyRentalRate = MonthlyOf(s),
                        ActualMonthlyRental = MonthlyOf(s),
                        WholeYearRental = MonthlyOf(s) * 12,
                        FishFeeTotal = null,
                        IsClosed = s.Status == StallStatus.Closed,
                        Arrangement = contract?.Arrangement ?? OccupancyArrangement.SignedContract
                    };
                }).ToList(),
                SectionMonthlyTotal = groupStalls.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionActualMonthly = groupStalls.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionWholeYearTotal = groupStalls.Where(s => s.Status == StallStatus.Active).Sum(s => MonthlyOf(s) * 12),
                SectionFishFeeTotal = 0
            });
        }

        // Handle truly section-less stalls (TCC, NCC, BBQ, ICE, SLH — no Section AND no custom section).
        var stallsWithoutSection = stalls.Where(s => !s.Section.HasValue && string.IsNullOrWhiteSpace(s.CustomSectionName)).ToList();
        if (stallsWithoutSection.Any())
        {
            sectionsWithSection.Add(new StallHoldersSectionDto
            {
                SectionName = "All Stalls",
                StallCount = stallsWithoutSection.Count,
                Rows = stallsWithoutSection.Select((s, idx) =>
                {
                    var contract = s.Contracts.FirstOrDefault(c => c.IsActive);
                    var durationYears = contract?.DurationYears ?? 0;
                    return new StallHolderRowDto
                    {
                        // The stall itself, so a caller can act on it rather than on its number: a number identifies a
                        // stall only within a facility and section, and the market has three spaces called "1".
                        StallId = s.Id,
                        RowNumber = idx + 1,
                        ActualOccupant = contract?.ActualOccupant ?? "",
                        NameOnContract = contract?.NameOnContract ?? "",
                        StallNo = s.StallNo,
                        EffectivityDate = contract?.EffectivityDate ?? default,
                        DurationYears = durationYears,
                        AreaSqm = s.AreaSqm,
                        MonthlyRentalRate = MonthlyOf(s),
                        ActualMonthlyRental = MonthlyOf(s),
                        WholeYearRental = MonthlyOf(s) * 12,
                        FishFeeTotal = null,
                        IsClosed = s.Status == StallStatus.Closed,
                        AreaLocation = s.AreaLocation?.ToString(),
                        Arrangement = contract?.Arrangement ?? OccupancyArrangement.SignedContract
                    };
                }).ToList(),
                SectionMonthlyTotal = stallsWithoutSection.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionActualMonthly = stallsWithoutSection.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionWholeYearTotal = stallsWithoutSection.Where(s => s.Status == StallStatus.Active).Sum(s => MonthlyOf(s) * 12),
                SectionFishFeeTotal = 0
            });
        }

        return new StallHoldersListDto
        {
            TotalStalls = stalls.Count,
            VegetableCount = stalls.Count(s => s.Section == MarketSection.VegetableArea),
            FishCount = stalls.Count(s => s.Section == MarketSection.FishSection),
            MeatCount = stalls.Count(s => s.Section == MarketSection.MeatSection),
            Sections = sectionsWithSection,
            GrandTotalActiveStalls = stalls.Count(s => s.Status == StallStatus.Active),
            GrandTotalMonthlyRate = stalls.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
            GrandTotalWholeYearRental = stalls.Where(s => s.Status == StallStatus.Active).Sum(s => MonthlyOf(s) * 12)
        };
    }
}
