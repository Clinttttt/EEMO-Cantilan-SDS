using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The registered-source browser behind Mobile New Collection: bounded, category-filtered pages of the sources this collector is assigned to,
/// by the same exact identities the finder uses. It states who may be collected from and nothing about amounts.
/// </summary>
public sealed partial class CollectionSessionTests
{
    [SkippableFact]
    public async Task Browsing_lists_the_assigned_registered_sources_by_exact_identity_without_merging_same_names()
    {
        var Today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var registered = (await Office(db, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), Today.Year, FishMeatVendorType.Fish, VendorRegistrationKind.Renew, "One Payer"))).Value!;
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(Today), null!, new NoMarketDays());

        var all = await sources.BrowseSourcesAsync(null, null, 0, 25, default);

        // The same person's NPM occupancy and fish-vendor registration are two sources, never one.
        Assert.Equal(2, all.Items.Count(x => x.DisplayName == "One Payer"));
        Assert.Contains(all.Items, x => x.Identity == new CollectionSourceIdentity(SourceIdentityKind.FishMeatVendorRegistration, registered.Id));
        Assert.Contains(all.Items, x => x.Identity.Kind == SourceIdentityKind.Occupancy);
        Assert.Equal(all.Items.Count, all.Total);
        // Only the categories this collector's assignments support exist: no space operation is assigned in this scenario.
        Assert.Contains(all.Categories, c => c.Key == CollectionSourceCategories.FishMeat);
        Assert.DoesNotContain(all.Categories, c => c.Key == CollectionSourceCategories.Spaces);
    }

    [SkippableFact]
    public async Task Browsing_filters_by_category_and_search_and_pages_in_bounded_steps()
    {
        var Today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var office = Office(db, w, "SuperAdmin");
        foreach (var name in new[] { "Ana Reyes", "Ben Cruz", "Carla Diaz" })
            Assert.True((await office.RegisterAsync(new(Guid.NewGuid(), Today.Year, FishMeatVendorType.Meat, VendorRegistrationKind.New, name))).IsSuccess);
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(Today), null!, new NoMarketDays());

        var fish = await sources.BrowseSourcesAsync(CollectionSourceCategories.FishMeat, null, 0, 2, default);
        Assert.Equal(2, fish.Items.Count); Assert.Equal(3, fish.Total); Assert.True(fish.HasMore(0));
        Assert.All(fish.Items, x => Assert.Equal(SourceIdentityKind.FishMeatVendorRegistration, x.Identity.Kind));
        Assert.Equal(["Ana Reyes", "Ben Cruz"], fish.Items.Select(x => x.DisplayName).ToArray());

        var next = await sources.BrowseSourcesAsync(CollectionSourceCategories.FishMeat, null, 2, 2, default);
        Assert.Equal(["Carla Diaz"], next.Items.Select(x => x.DisplayName).ToArray()); Assert.False(next.HasMore(2));

        var searched = await sources.BrowseSourcesAsync(CollectionSourceCategories.FishMeat, "ben", 0, 25, default);
        Assert.Equal("Ben Cruz", Assert.Single(searched.Items).DisplayName);

        // A category the collector is not assigned to returns nothing rather than another category's sources.
        var spaces = await sources.BrowseSourcesAsync(CollectionSourceCategories.Spaces, null, 0, 25, default);
        Assert.Empty(spaces.Items); Assert.Equal(0, spaces.Total);
    }

    [SkippableFact]
    public async Task Browsing_never_crosses_the_tenant_or_the_collector_and_the_workflow_bounds_every_page()
    {
        var Today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        Assert.True((await Office(db, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), Today.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Hidden Vendor"))).IsSuccess);

        var other = new CollectionSessionSources(db, new Caller(w.CollectorId, Guid.NewGuid()), new Tenant(Guid.NewGuid()), new Clock(Today), null!, new NoMarketDays());
        var foreign = await other.BrowseSourcesAsync(null, null, 0, 25, default);
        Assert.Empty(foreign.Items); Assert.Empty(foreign.Categories);

        var flow = NativeWorkflow(db, w, new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(Today), null!, new NoMarketDays()));
        Assert.Equal(ResultStatus.Invalid, (await flow.BrowseSourcesAsync(null, null, 0, CollectionSourceCategories.MaxPage + 1)).Status);
        Assert.Equal(ResultStatus.Invalid, (await flow.BrowseSourcesAsync(null, null, -1, 10)).Status);
        Assert.True((await flow.BrowseSourcesAsync(null, null, 0, CollectionSourceCategories.MaxPage)).IsSuccess);
        Assert.Empty(await db.Collections.ToListAsync());                // browsing records and changes nothing
    }
}
