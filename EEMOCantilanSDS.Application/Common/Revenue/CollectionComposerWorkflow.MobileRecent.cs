using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// What the Collector sees before taking another utility payment: the canonical Collections already posted against this exact
/// source (one stall's utility bill for the period). It reads the posted allocations and corrections themselves, so it can never
/// diverge from the SRC ledger, never matches by payer name, and never invents history.
/// </summary>
public sealed partial class CollectionComposerWorkflow
{
    private const int RecentUtilityTake = 5;

    public async Task<Result<IReadOnlyList<RecentUtilityCollectionDto>>> GetMobileRecentUtilityCollectionsAsync(
        Guid stallId, UtilityKind kind, int year, int month, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collectorId)
            return Result<IReadOnlyList<RecentUtilityCollectionDto>>.Forbidden();
        var tenant = municipality.MunicipalityId;
        if (tenant == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenant)
            return Result<IReadOnlyList<RecentUtilityCollectionDto>>.Forbidden();
        if (!Enum.IsDefined(kind) || year is < 2000 or > 2200 || month is < 1 or > 12 || stallId == Guid.Empty)
            return Result<IReadOnlyList<RecentUtilityCollectionDto>>.Failure("Choose a valid source and billing month.", ResultStatus.Invalid);

        var assigned = await AssignedFacilitiesAsync(tenant, collectorId, ct);
        if (assigned.Count == 0) return Result<IReadOnlyList<RecentUtilityCollectionDto>>.Forbidden();
        // A source of a facility the collector is not assigned to is reported as absent, never as a refusal that confirms it exists.
        var facility = await db.Stalls.AsNoTracking().Where(x => x.MunicipalityId == tenant && x.Id == stallId)
            .Select(x => (FacilityCode?)x.Facility!.Code).SingleOrDefaultAsync(ct);
        if (facility is not { } code || !assigned.Contains(code)) return Result<IReadOnlyList<RecentUtilityCollectionDto>>.NotFound();

        var part = kind == UtilityKind.Electricity ? CollectionSourcePart.Electricity : CollectionSourcePart.Water;
        var billIds = db.UtilityBills.AsNoTracking()
            .Where(x => x.MunicipalityId == tenant && x.StallId == stallId && x.BillingYear == year && x.BillingMonth == month)
            .Select(x => x.Id);
        var rows = await (from allocation in db.CollectionAllocations.AsNoTracking()
                          join line in db.CollectionLines.AsNoTracking() on allocation.CollectionLineId equals line.Id
                          join collection in db.Collections.AsNoTracking() on line.CollectionId equals collection.Id
                          where allocation.MunicipalityId == tenant && collection.MunicipalityId == tenant
                              && allocation.SourceKind == CollectionSourceKind.UtilityBill && allocation.SourcePart == part
                              && billIds.Contains(allocation.SourceId)
                          orderby collection.RecordedAtUtc descending
                          select new
                          {
                              collection.Id, collection.ReferenceCode, collection.BusinessDate, collection.RecordedAtUtc, collection.ActorName,
                              allocation.Amount
                          }).Take(RecentUtilityTake * 4).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).Distinct().ToArray();
        var reversed = (await db.CollectionCorrections.AsNoTracking()
                .Where(x => x.MunicipalityId == tenant && ids.Contains(x.OriginalCollectionId) && x.FinancialEffectAmount < 0m)
                .Select(x => x.OriginalCollectionId).ToListAsync(ct)).ToHashSet();
        var result = rows.GroupBy(x => x.Id).Select(g => g.First() with { Amount = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.RecordedAtUtc).Take(RecentUtilityTake)
            .Select(x => new RecentUtilityCollectionDto(x.Id, x.ReferenceCode, x.Amount, x.BusinessDate, x.RecordedAtUtc,
                reversed.Contains(x.Id) ? "Reversed" : "Posted", x.ActorName))
            .ToList();
        return Result<IReadOnlyList<RecentUtilityCollectionDto>>.Success(result);
    }
}
