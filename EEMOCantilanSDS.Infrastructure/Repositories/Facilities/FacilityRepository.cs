using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class FacilityRepository(AppDbContext context) : IFacilityRepository
{
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
    public async Task<IReadOnlyList<ConfiguredFacilityDto>> GetConfiguredFacilitiesAsync(CancellationToken ct)
    {
        var today = PhilippineTime.Today;

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

        return facilities.Select(f => new ConfiguredFacilityDto(
            f.Code.ToString(),
            f.Name,
            f.ShortName,
            f.Description,
            FacilityDisplay.BillingModel(f.Archetype),
            f.IsActive,
            f.StallCount,
            currentRates
                .Where(r => r.FacilityCode == f.Code)
                .OrderBy(r => r.RateKey)
                .Select(r => new FacilityRateLineDto(FacilityDisplay.RateLabel(r.RateKey), r.Amount))
                .ToList())).ToList();
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
            .OrderBy(f => f.Code)
            .Select(f => new
            {
                f.Code,
                f.Name,
                f.ShortName,
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
                    && monthStart <= c.EffectivityDate.AddYears(c.DurationYears))))).ToList();
    }
}
