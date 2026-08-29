using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class FacilityRepository(AppDbContext context, IClock clock) : IFacilityRepository
{
    /// <summary>Test/non-DI convenience, matching the other repositories: reads the real clock.</summary>
    public FacilityRepository(AppDbContext context) : this(context, new SystemClock()) { }
    public async Task<Facility?> GetByCodeAsync(FacilityCode facilityCode, CancellationToken ct)
    {
        return await context.Facilities.FirstOrDefaultAsync(f => f.Code == facilityCode, ct);
    }

    public async Task<IReadOnlyDictionary<FacilityCode, string>> GetFacilityNamesAsync(CancellationToken ct)
    {
        // Seeded facility names are the single source of truth for display labels.
        // Soft-deleted rows are excluded by the global query filters.
        return await context.Facilities
            .AsNoTracking()
            .ToDictionaryAsync(f => f.Code, f => f.Name, ct);
    }
    public async Task AddFacilityAsync(Facility facility, CancellationToken ct)
    {
        await context.Facilities.AddAsync(facility, ct);
    }

    public async Task<IReadOnlyList<NpmCustomSectionDto>> GetNpmCustomSectionsAsync(CancellationToken ct)
    {
        var npm = await context.Facilities.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == FacilityCode.NPM, ct);
        if (npm is null) return Array.Empty<NpmCustomSectionDto>();

        // Stall count per custom section name (all non-deleted stalls — incl. Closed — so a section that
        // still has ANY stall can't be removed and orphan it). The query filter scopes to this tenant.
        var counts = await context.Stalls.AsNoTracking()
            .Where(s => s.FacilityId == npm.Id && s.Section == null && s.CustomSectionName != null)
            .GroupBy(s => s.CustomSectionName!)
            .Select(g => new { Name = g.Key!, Count = g.Count() })
            .ToListAsync(ct);

        // Registry names UNION distinct stall names (legacy stalls made before the registry existed),
        // de-duplicated case-insensitively.
        var names = new List<string>();
        void AddName(string? raw)
        {
            var n = (raw ?? string.Empty).Trim();
            if (n.Length > 0 && !names.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase)))
                names.Add(n);
        }
        foreach (var r in npm.CustomSectionNames) AddName(r);
        foreach (var c in counts) AddName(c.Name);

        // The fee the office has stated for each section, as of today: the latest row on or before it. Read here rather
        // than through the fee snapshot because this is the office's own configuration screen and wants the figure it
        // stated, section by section, not a fee resolved for a particular stall.
        var today = clock.PhilippineToday;
        var rates = await context.FacilitySectionRates.AsNoTracking()
            .Where(r => r.FacilityCode == FacilityCode.NPM && r.EffectiveDate <= today)
            .Select(r => new { r.SectionName, r.Amount, r.EffectiveDate })
            .ToListAsync(ct);

        decimal? StatedRateFor(string name)
        {
            var latest = rates
                .Where(r => string.Equals(r.SectionName.Trim(), name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.EffectiveDate)
                .Select(r => (decimal?)r.Amount)
                .FirstOrDefault();

            // Nought is a withdrawn figure, not a price, so it reads as no stated rate — the same reading the billing
            // rule applies, and the two must not disagree on the office's own configuration screen.
            return latest is > 0m ? latest : null;
        }

        // Whether a stall recorded in each section is usually metered. A default for the form, so it bills nothing and is
        // read as the office's current answer rather than as a history.
        var utilities = await context.FacilitySectionUtilities.AsNoTracking()
            .Where(u => u.FacilityCode == FacilityCode.NPM)
            .Select(u => new { u.SectionName, u.Electricity, u.Water })
            .ToListAsync(ct);

        return names
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n =>
            {
                var metered = utilities.FirstOrDefault(u =>
                    string.Equals(u.SectionName.Trim(), n, StringComparison.OrdinalIgnoreCase));

                return new NpmCustomSectionDto(
                    n,
                    counts.Where(c => string.Equals(c.Name.Trim(), n, StringComparison.OrdinalIgnoreCase)).Sum(c => c.Count),
                    StatedRateFor(n),
                    metered?.Electricity ?? false,
                    metered?.Water ?? false);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ConfiguredFacilityDto>> GetConfiguredFacilitiesAsync(CancellationToken ct)
    {
        var today = clock.PhilippineToday;

        // Facilities for the caller's tenant (global query filter scopes to the current LGU and excludes
        // soft-deleted rows). Stall count = configured units (0 for per-head/per-trip/weekly facilities).
        var facilities = await context.Facilities
            .AsNoTracking()
            .OrderBy(f => f.Code)
            .Select(f => new
            {
                f.Code,
                f.Name,
                f.ShortName,
                f.Description,
                f.Archetype,
                f.IsActive,
                f.VegetableSectionLabel,
                f.FishSectionLabel,
                f.MeatSectionLabel,
                StallCount = f.Stalls.Count()
            })
            .ToListAsync(ct);

        // Current fixed rates: the latest effective row per (facility, key) as of today. Tenant-scoped by
        // the query filter; effective-dating keeps history intact for settled periods.
        var rates = await context.FacilityRates
            .AsNoTracking()
            .Where(r => r.EffectiveDate <= today)
            .Select(r => new { r.FacilityCode, r.RateKey, r.Amount, r.EffectiveDate })
            .ToListAsync(ct);

        var currentRates = rates
            .GroupBy(r => new { r.FacilityCode, r.RateKey })
            .Select(g => g.OrderByDescending(x => x.EffectiveDate).First())
            .ToList();

        return facilities.Select(f =>
        {
            // Every applicable key for this facility, so the config view is complete rather than only the keys
            // that happen to have a row. An amount the office has NOT stated shows as nothing: the drawer used to
            // display the reference municipality's constant here, tagged as a default, which read as a rate in
            // force in this office and was in fact the figure it was being billed. Monthly-rental facilities have
            // no fixed keys.
            var lines = FacilityRateKeys.For(f.Code).Select(key =>
            {
                var row = currentRates.FirstOrDefault(r => r.FacilityCode == f.Code && r.RateKey == key);
                var amount = row?.Amount ?? 0m;
                return new ConfiguredRateDto(key.ToString(), FacilityDisplay.RateLabel(key), amount, row is not null);
            }).ToList();

            return new ConfiguredFacilityDto(
                f.Code.ToString(),
                f.Name,
                f.ShortName,
                f.Description,
                FacilityDisplay.BillingModel(f.Archetype),
                f.IsActive,
                f.StallCount,
                lines,
                f.VegetableSectionLabel,
                f.FishSectionLabel,
                f.MeatSectionLabel);
        }).ToList();
    }

    public async Task<FacilitySummaryDto> GetSummaryAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var facility = await context.Facilities
            .AsNoTracking()
            .Include(f => f.Stalls)
                .ThenInclude(s => s.PaymentRecords.Where(p => p.BillingYear == year && p.BillingMonth == month))
            .FirstOrDefaultAsync(f => f.Code == facilityCode, ct);

        if (facility == null)
            return new FacilitySummaryDto(0, 0, 0, 0);

        var activeStalls = facility.Stalls.Where(s => s.Status == StallStatus.Active).ToList();
        var totalStalls = activeStalls.Count;

        var payments = activeStalls.SelectMany(s => s.PaymentRecords).ToList();

        var totalCollected = payments.Sum(p => p.AmountPaid);
        var totalPending = payments.Sum(p => p.BalanceDue);

        var collectionRate = totalStalls > 0 
            ? (decimal)payments.Count(p => p.Status == PaymentStatus.Paid) / totalStalls * 100 
            : 0;

        return new FacilitySummaryDto(totalCollected, totalPending, collectionRate, totalStalls);
    }

    public async Task<IReadOnlyList<FacilitySidebarSummaryDto>> GetSidebarSummariesAsync(int year, int month, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Unpaid = active, occupied stalls with no Paid record for the month — where "occupied" means a
        // contract whose TERM covers the month (active AND EffectivityDate ≤ monthEnd ≤ ExpiryDate), i.e.
        // Contract.OverlapsPeriod. This EXCLUDES payors whose contract has already expired (IsActive alone
        // would wrongly keep them). Expiry (EffectivityDate.AddYears(DurationYears)) is evaluated in memory
        // to avoid unreliable SQL date-arithmetic translation; only minimal columns are projected first.
        // Soft-deleted rows are excluded by the global query filters.
        var facilities = await context.Facilities
            .AsNoTracking()
            .Where(f => f.IsActive)   // deactivated facilities are hidden from the operational menu (sidebar/tabs)
            .OrderBy(f => f.Code)
            .Select(f => new
            {
                f.Code,
                f.Name,
                f.ShortName,
                f.VegetableSectionLabel,
                f.FishSectionLabel,
                f.MeatSectionLabel,
                Stalls = f.Stalls
                    .Where(s => s.Status == StallStatus.Active)
                    .Select(s => new
                    {
                        Contracts = s.Contracts.Select(c => new { c.IsActive, c.EffectivityDate, c.DurationYears }),
                        HasPaid = s.PaymentRecords.Any(p => p.BillingYear == year
                            && p.BillingMonth == month
                            && p.Status == PaymentStatus.Paid)
                    })
            })
            .ToListAsync(ct);

        return facilities.Select(f => new FacilitySidebarSummaryDto(
            f.Code,
            f.Name,
            f.ShortName,
            f.Stalls.Count(s => !s.HasPaid
                && s.Contracts.Any(c => c.IsActive
                    && c.EffectivityDate <= monthEnd
                    && monthStart <= c.EffectivityDate.AddYears(c.DurationYears))),
            f.VegetableSectionLabel,
            f.FishSectionLabel,
            f.MeatSectionLabel)).ToList();
    }
}
