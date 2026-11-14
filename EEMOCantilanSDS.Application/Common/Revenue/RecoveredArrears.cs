using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common.Billing;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>One rent allocation of a posted Collection, with the facility its rent came from and whether it reports as recovered Arrears.</summary>
public sealed record PlacedRentAllocation(Guid Id, Guid LineId, DateOnly BusinessDate, decimal Amount, FacilityCode? Facility, bool Arrears)
{
    public string ClassificationCode => Arrears ? RevenueClassificationCodes.Arrears : RevenueClassificationCodes.PermanentStallRent;
}

/// <summary>
/// Where collected stall rent reports on the Monthly Income (IA-020, IA-069, IA-076). Rent for a month that was already Arrears when the cash came in reports on the
/// Arrears row; all other rent on its facility's row. This is placement on the report only: the Collection, its line, its receipt and the totals never change, and
/// the source record keeps the facility, occupancy and month the debt came from.
/// </summary>
public static class RecoveredArrears
{
    /// <summary>
    /// Whether rent for this month was Arrears on the day it was collected. A term counts as ended by its dates and never by its present active flag, because
    /// that flag says what is true today: reading it would turn rent paid on time into Arrears the moment the term was later renewed.
    /// </summary>
    public static bool Qualifies(Stall? stall, int year, int month, DateOnly cashDate) =>
        ReceivableClassification.Classify(year, month, cashDate, stall?.OccupancyAnsweringForMonth(year, month, cashDate)?.End < cashDate) == ReceivableCategory.Arrears;

    /// <summary>The rent allocations of these lines, placed. A line whose source cannot be read stays on its facility's row, so no money is dropped.</summary>
    public static async Task<IReadOnlyList<PlacedRentAllocation>> PlaceAsync(IAppDbContext db, Guid tenantId, IReadOnlyCollection<Guid> rentLineIds, CancellationToken ct)
    {
        if (rentLineIds.Count == 0) return [];

        var allocations = await (
            from allocation in db.CollectionAllocations.AsNoTracking()
            join line in db.CollectionLines.AsNoTracking() on new { allocation.MunicipalityId, Id = allocation.CollectionLineId } equals new { line.MunicipalityId, line.Id }
            join collection in db.Collections.AsNoTracking() on new { line.MunicipalityId, Id = line.CollectionId } equals new { collection.MunicipalityId, collection.Id }
            where allocation.MunicipalityId == tenantId && rentLineIds.Contains(allocation.CollectionLineId)
                && (allocation.SourceKind == CollectionSourceKind.PaymentRecord || allocation.SourceKind == CollectionSourceKind.DailyCollection
                    || allocation.SourceKind == CollectionSourceKind.NpmRentMonth)
            select new { allocation.Id, allocation.CollectionLineId, allocation.SourceId, collection.BusinessDate, allocation.Amount }).ToListAsync(ct);

        var sourceIds = allocations.Select(x => x.SourceId).Distinct().ToList();
        var sources = new Dictionary<Guid, (Guid Stall, int Year, int Month)>();
        foreach (var r in await db.PaymentRecords.AsNoTracking().Where(x => x.MunicipalityId == tenantId && sourceIds.Contains(x.Id))
            .Select(x => new { x.Id, x.StallId, x.BillingYear, x.BillingMonth }).ToListAsync(ct))
            sources[r.Id] = (r.StallId, r.BillingYear, r.BillingMonth);
        foreach (var d in await db.DailyCollections.AsNoTracking().Where(x => x.MunicipalityId == tenantId && sourceIds.Contains(x.Id))
            .Select(x => new { x.Id, x.StallId, x.CollectionDate }).ToListAsync(ct))
            sources[d.Id] = (d.StallId, d.CollectionDate.Year, d.CollectionDate.Month);
        foreach (var m in await db.NpmRentMonths.AsNoTracking().Where(x => x.MunicipalityId == tenantId && sourceIds.Contains(x.Id))
            .Select(x => new { x.Id, x.StallId, x.PeriodStart }).ToListAsync(ct))
            sources[m.Id] = (m.StallId, m.PeriodStart.Year, m.PeriodStart.Month);

        var stallIds = sources.Values.Select(x => x.Stall).Distinct().ToList();
        var stalls = await db.Stalls.AsNoTracking().Include(s => s.Facility).Include(s => s.Contracts)
            .Where(s => s.MunicipalityId == tenantId && stallIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        return allocations.Select(a =>
        {
            if (!sources.TryGetValue(a.SourceId, out var source) || !stalls.TryGetValue(source.Stall, out var stall))
                return new PlacedRentAllocation(a.Id, a.CollectionLineId, a.BusinessDate, a.Amount, null, false);
            return new PlacedRentAllocation(a.Id, a.CollectionLineId, a.BusinessDate, a.Amount, stall.Facility?.Code,
                Qualifies(stall, source.Year, source.Month, a.BusinessDate));
        }).ToList();
    }
}
