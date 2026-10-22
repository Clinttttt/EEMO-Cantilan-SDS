using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

// Partial of CollectionSessionSources: the NPM occupancy an office-confirmed association names for a registered vendor. A name never links
// anything; only the office's confirmed association does, and only while that exact occupancy still answers for its stall and the collector is
// assigned to the market. A vendor's own session may collect that stall's NPM rent and utilities as items of the same checkout (each item is its own
// Collection, quoted and posted by the stall's own source writer, so no payer or balance is merged); the vendor session answers for no other stall.
public sealed partial class CollectionSessionSources
{
    private sealed record LinkedOccupancy(Stall Stall, Contract Answering, string OccupantSnapshot);

    private async Task<IReadOnlyList<LinkedOccupancy>> LinkedOccupanciesAsync(Guid vendorRegistrationId, DateOnly date, CancellationToken ct)
    {
        var assignedToMarket = await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == Tenant && x.Id == user.CollectorId && x.IsActive
            && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM), ct);
        if (!assignedToMarket) return [];

        // A correction withdraws the link: only a confirmation that has not been corrected is offered.
        var associations = await db.FishMeatNpmAssociations.AsNoTracking()
            .Where(x => x.MunicipalityId == Tenant && x.VendorRegistrationId == vendorRegistrationId && x.CorrectedAtUtc == null)
            .OrderBy(x => x.ConfirmedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var linked = new List<LinkedOccupancy>();
        foreach (var association in associations)
        {
            var stall = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts)
                .SingleOrDefaultAsync(x => x.MunicipalityId == Tenant && x.Id == association.StallId && x.Facility!.Code == FacilityCode.NPM, ct);
            var answering = stall?.OccupancyAnsweringForMonth(date.Year, date.Month, date)?.Contract;
            if (stall is null || answering is null || answering.Id != association.ContractId) continue;   // the stall has since passed to another occupancy
            linked.Add(new(stall, answering, association.OccupantSnapshot));
        }
        return linked;
    }

    private async Task<IReadOnlyList<CollectionSourceSearchResult>> LinkedNpmSourcesAsync(Guid vendorRegistrationId, DateOnly date, CancellationToken ct)
    {
        var linked = new List<CollectionSourceSearchResult>();
        foreach (var occupancy in await LinkedOccupanciesAsync(vendorRegistrationId, date, ct))
        {
            // The stall's own discovery decides which utilities it offers this collector today, so what a vendor's picker advertises is
            // exactly what the stall's picker will then offer. Nothing is inferred from the vendor, and no utility is invented for it.
            var stallOffers = await DiscoverNativeAsync(new(SourceIdentityKind.Occupancy, occupancy.Answering.Id), date, ct);
            var utilities = stallOffers.Operations.Where(x => x.OperationCode is "ECF" or "WCF").Select(x => x.OperationCode).Distinct().Order().ToArray();
            linked.Add(new(new(SourceIdentityKind.Occupancy, occupancy.Answering.Id), occupancy.Answering.ActualOccupant ?? occupancy.OccupantSnapshot,
                $"{occupancy.Stall.Facility!.Name} · Stall {occupancy.Stall.StallNo}", "FACILITY_" + occupancy.Stall.Facility.Code, Utilities: utilities.Length == 0 ? null : utilities));
        }
        return linked;
    }

    /// <summary>
    /// Whether this checkout's source answers for the occupancy an item belongs to: an occupancy source answers for itself; a registered vendor
    /// answers for the occupancies the office has linked to it, and for nothing else.
    /// </summary>
    private async Task<bool> SessionAnswersForOccupancyAsync(CollectionSessionIntent session, Guid contractId, CancellationToken ct) => session.SourceIdentity switch
    {
        { Kind: SourceIdentityKind.Occupancy } identity => identity.Id == contractId,
        { Kind: SourceIdentityKind.FishMeatVendorRegistration } identity =>
            (await LinkedOccupanciesAsync(identity.Id, session.BusinessDate, ct)).Any(x => x.Answering.Id == contractId),
        _ => false
    };
}
