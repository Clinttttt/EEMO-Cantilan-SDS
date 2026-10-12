using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

// Partial of CollectionSessionSources: the NPM occupancy an office-confirmed association names for a registered vendor, offered ONLY so
// the collector can open that stall as its own, separate collection. It is navigation and nothing else: no amount, no balance, no shared
// checkout, no merged payer. A name never links anything; only the office's confirmed association does, and only while that exact
// occupancy still answers for its stall and the collector is assigned to the market.
public sealed partial class CollectionSessionSources
{
    private async Task<IReadOnlyList<CollectionSourceSearchResult>> LinkedNpmSourcesAsync(Guid vendorRegistrationId, DateOnly date, CancellationToken ct)
    {
        var assignedToMarket = await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.Id == user.CollectorId && x.IsActive
            && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM), ct);
        if (!assignedToMarket) return [];

        // A correction withdraws the link: only a confirmation that has not been corrected is offered.
        var associations = await db.FishMeatNpmAssociations.AsNoTracking()
            .Where(x => x.MunicipalityId == Tenant && x.VendorRegistrationId == vendorRegistrationId && x.CorrectedAtUtc == null)
            .OrderBy(x => x.ConfirmedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var linked = new List<CollectionSourceSearchResult>();
        foreach (var association in associations)
        {
            var stall = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts)
                .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == association.StallId && x.Facility!.Code == FacilityCode.NPM, ct);
            var answering = stall?.OccupancyAnsweringForMonth(date.Year, date.Month, date)?.Contract;
            if (stall is null || answering is null || answering.Id != association.ContractId) continue;   // the stall has since passed to another occupancy
            // The stall's own discovery decides which utilities it offers this collector today, so what a vendor's picker advertises is
            // exactly what the stall's picker will then offer. Nothing is inferred from the vendor, and no utility is invented for it.
            var stallOffers = await DiscoverNativeAsync(new(SourceIdentityKind.Occupancy, answering.Id), date, ct);
            var utilities = stallOffers.Operations.Where(x => x.OperationCode is "ECF" or "WCF").Select(x => x.OperationCode).Distinct().Order().ToArray();
            linked.Add(new(new(SourceIdentityKind.Occupancy, answering.Id), answering.ActualOccupant ?? association.OccupantSnapshot,
                $"{stall.Facility!.Name} · Stall {stall.StallNo}", "FACILITY_" + stall.Facility.Code, Utilities: utilities.Length == 0 ? null : utilities));
        }
        return linked;
    }
}
