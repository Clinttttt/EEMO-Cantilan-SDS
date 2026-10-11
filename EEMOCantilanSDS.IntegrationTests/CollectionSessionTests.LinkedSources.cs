using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// A registered vendor may be linked by the office to the NPM stall they occupy. The collector is offered that stall to open as its own,
/// separate collection — navigation only. Nothing is linked by name, nothing is merged, and no checkout spans two sources.
/// </summary>
public sealed partial class CollectionSessionTests
{
    [SkippableFact]
    public async Task A_confirmed_association_offers_the_linked_NPM_stall_as_navigation_only_and_never_by_name()
    {
        var w = await SeedAsync(section: MarketSection.FishSection); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var head = Office(db, w, "SuperAdmin"); var year = PhilippineTime.Today.Year;
        var source = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        var op = Guid.NewGuid();
        var linked = (await head.RegisterAsync(new(op, year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Linked Vendor", NpmAssociation: Confirmation(source, op)))).Value!;
        // The same NAME as the stall's occupant, but no confirmation: a name never links anything.
        var sameName = (await head.RegisterAsync(new(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New, source.Occupant))).Value!;
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(PhilippineTime.Today), null!, new NoMarketDays());
        var flow = NativeWorkflow(db, w, sources);

        var discovery = (await flow.DiscoverNativeAsync(new(SourceIdentityKind.FishMeatVendorRegistration, linked.Id))).Value!;

        var offered = Assert.Single(discovery.LinkedSources!);
        Assert.Equal(new CollectionSourceIdentity(SourceIdentityKind.Occupancy, source.ContractId), offered.Identity);
        Assert.Contains("Stall", offered.Context);
        Assert.Equal("FACILITY_NPM", offered.OperationCode);
        Assert.DoesNotContain(discovery.Operations, x => x.Kind == CollectionSessionItemKind.NpmDaily || x.Kind == CollectionSessionItemKind.NpmWholePayment || x.Kind == CollectionSessionItemKind.NpmPartPayment);   // no NPM money under the vendor's session
        Assert.All(discovery.Operations, x => Assert.Contains(x.OperationCode, new[] { CollectorOperationCodes.FishMeatVendorFee, CollectorOperationCodes.WeightAndMeasure }));

        var byName = (await flow.DiscoverNativeAsync(new(SourceIdentityKind.FishMeatVendorRegistration, sameName.Id))).Value!;
        Assert.Null(byName.LinkedSources);                                  // the same name with no office confirmation offers nothing

        // A vendor's session cannot carry the stall's NPM payment: the stall is a different source.
        var intent = new CollectionSessionIntent(Guid.NewGuid(), PhilippineTime.Today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.NpmDaily, 30m, NpmDaily: new(source.StallId))],
            SourceIdentity: new(SourceIdentityKind.FishMeatVendorRegistration, linked.Id));
        Assert.False((await flow.QuoteAsync(intent)).Value!.CanRecord);
        Assert.Empty(await db.Collections.ToListAsync());                   // discovery recorded and changed nothing
    }

    [SkippableFact]
    public async Task A_corrected_association_is_withdrawn_from_navigation()
    {
        var w = await SeedAsync(section: MarketSection.MeatSection); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var head = Office(db, w, "SuperAdmin"); var year = PhilippineTime.Today.Year;
        var source = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        var op = Guid.NewGuid();
        var vendor = (await head.RegisterAsync(new(op, year, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Linked Meat Vendor", NpmAssociation: Confirmation(source, op)))).Value!;
        var flow = NativeWorkflow(db, w, new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(PhilippineTime.Today), null!, new NoMarketDays()));
        Assert.NotNull((await flow.DiscoverNativeAsync(new(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id))).Value!.LinkedSources);

        var link = await db.FishMeatNpmAssociations.SingleAsync();
        Assert.True((await head.CorrectNpmVendorLinkAsync(link.Id, new(Guid.NewGuid(), "Office verified incorrect association"))).IsSuccess);

        Assert.Null((await flow.DiscoverNativeAsync(new(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id))).Value!.LinkedSources);
    }
}
