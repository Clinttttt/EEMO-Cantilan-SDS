using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Common.Revenue;
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
    public async Task Npm_occupancy_without_manual_utility_flags_is_discoverable_without_creating_bills()
    {
        var today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false);
        await using var db = database.CreateContext(w.TenantId);
        var stall = await db.Stalls.SingleAsync(x => x.Id == w.StallId);
        stall.SetApplicableFees(ApplicableFees.BaseRental, "Synthetic registration without utility checkboxes");
        db.Add(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "Synthetic Head"));
        db.Add(CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, today, w.CollectorId, "Synthetic Head", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var contract = await db.Contracts.SingleAsync(x => x.StallId == w.StallId);
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(today), null!, new NoMarketDays());
        var result = await NativeWorkflow(db, w, sources).DiscoverNativeAsync(new(SourceIdentityKind.Occupancy, contract.Id));
        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains(result.Value!.Operations, x => x.OperationCode == "ECF");
        Assert.Contains(result.Value.Operations, x => x.OperationCode == "WCF");
        foreach (var kind in new[] { UtilityKind.Electricity, UtilityKind.Water })
        {
            var head = new UtilityRegisterReader(db, new Caller(w.CollectorId, w.TenantId, "SuperAdmin"), new Tenant(w.TenantId), new Clock(today));
            var register = await head.GetAsync(new(kind, today.Year, today.Month));
            Assert.True(register.IsSuccess, register.Error);
            Assert.Single(register.Value!.Rows);
        }
        Assert.Empty(await db.UtilityBills.ToListAsync());
        Assert.Empty(await db.Collections.ToListAsync());
    }

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
        Assert.Contains(all.Categories, c => c.Key == CollectionSourceCategories.Npm);
        Assert.DoesNotContain(all.Categories, c => c.Key == CollectionSourceCategories.Rentals);
        var npm = await sources.BrowseSourcesAsync(CollectionSourceCategories.Npm, null, 0, 25, default);
        Assert.Single(npm.Items);
        Assert.All(npm.Items, x => Assert.Equal(SourceIdentityKind.Occupancy, x.Identity.Kind));
        Assert.Equal("Fish Area", npm.Items[0].SectionLabel); // utility grouping uses the source's exact NPM section
        var unassigned = await sources.BrowseSourcesAsync(CollectionSourceCategories.Tcc, null, 0, 25, default);
        Assert.Empty(unassigned.Items);
        Assert.DoesNotContain(all.Categories, c => c.Key == CollectionSourceCategories.Spaces);
    }

    [SkippableFact]
    public async Task Browsing_preserves_custom_NPM_section_for_utility_source_filters()
    {
        var today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false);
        await using var db = database.CreateContext(w.TenantId);
        await EnableOffice(db, w);
        // Simulate a Head-declared custom NPM area; filtering must not assume only Fish/Meat.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Stalls\" SET \"Section\" = NULL, \"CustomSectionName\" = {"Extension"} WHERE \"Id\" = {w.StallId}");
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId),
            new Tenant(w.TenantId), new Clock(today), null!, new NoMarketDays());
        var page = await sources.BrowseSourcesAsync(CollectionSourceCategories.Npm, null, 0, 25, default);
        Assert.Equal("Extension", Assert.Single(page.Items).SectionLabel);
        var lookup = await sources.SearchSourcesAsync("One Payer", default);
        Assert.Contains(lookup, x => x.Identity.Kind == SourceIdentityKind.Occupancy && x.SectionLabel == "Extension");
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
    public async Task ARenewedSpacesEarlierTermMonths_ReadAsArrearsOnTheCollectorsChoice_WithoutChangingWhatIsCollectedOrHowItIsKeyed()
    {
        var today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false);
        await using var db = database.CreateContext(w.TenantId);
        var classification = RevenueClassification.Create(ObligationAccount.ClassificationCodeFor(ObligationKind.KanmanggaySpaceRental), w.TenantId);
        db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2020, 1, 1), "Space", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.KanmanggaySpaceRental, "Head"));
        await db.SaveChangesAsync();

        // A signed term 2021-2023, renewed from 2024 for three years; one month owed in each term, and one current-year month.
        var open = await SpaceTestKit.AddAsync(database, w.TenantId, "7", new DateOnly(2021, 1, 1), signed: true, amount: 1000m);
        foreach (var month in new[] { new DateOnly(2023, 11, 1), new DateOnly(2024, 2, 1), new DateOnly(today.Year, 1, 1) })
            await SpaceTestKit.AssessAsync(database, w.TenantId, open.Account, open.Rate, month);
        await using (var renewalContext = database.CreateContext(w.TenantId))
        {
            var workflow = SpaceTestKit.Workflow(renewalContext, w.TenantId);
            var command = new EEMOCantilanSDS.Application.Common.Lifecycle.LifecycleCommand(EEMOCantilanSDS.Application.Common.Lifecycle.LifecycleAction.Renew,
                new DateOnly(2024, 1, 1), "Renewal", "Res. 1", TermYears: 3, ContractName: "Signed Lessee", Reference: "KM-2");
            var preview = await workflow.PreviewAsync(open.Account.Id, command);
            Assert.True((await workflow.ExecuteAsync(open.Account.Id, new(Guid.NewGuid(), preview.Value!.Version, command))).IsSuccess);
        }

        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(today), null!, new NoMarketDays());
        var identity = new CollectionSourceIdentity(SourceIdentityKind.SpaceAccount, open.Account.Id);
        async Task<Dictionary<DateOnly, CollectionSessionSourceChoice>> ChoicesAsync() =>
            (await sources.DiscoverNativeAsync(identity, today, default)).Operations.SelectMany(o => o.Choices ?? []).ToDictionary(c => c.Identity.PeriodStart!.Value);

        var before = await ChoicesAsync();
        var earlierMonth = new DateOnly(2023, 11, 1); var currentTermMonth = new DateOnly(2024, 2, 1); var thisYear = new DateOnly(today.Year, 1, 1);
        // The collector sees why, per original billing month; the amount and key are the server's own, unchanged by the label.
        Assert.EndsWith("· Arrears, expired contract", before[earlierMonth].Context);
        Assert.EndsWith("· Arrears, previous year", before[currentTermMonth].Context);
        Assert.DoesNotContain("Arrears", before[thisYear].Context);
        Assert.All(before.Values, c => Assert.Equal((CollectionSessionItemKind.Obligation, 1000m), (c.Kind, c.ServerAmount)));
        Assert.StartsWith($"{CollectorOperationCodes.KanmanggaySpaceRental}|{open.Account.Id:N}|2023-11-01", before[earlierMonth].SelectionKey);
        Assert.Equal((open.Account.Id, 2023, 11), (before[earlierMonth].Identity.AccountId!.Value, before[earlierMonth].Identity.Year!.Value, before[earlierMonth].Identity.Month!.Value));

        await using var beforeContext = database.CreateContext(w.TenantId);
        var termsBefore = (await new ObligationWorkflow(beforeContext, new Caller(w.CollectorId, w.TenantId, "SuperAdmin"), new Tenant(w.TenantId), new Clock(today))
            .GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!.Single().Terms!;
        // Collecting part of the earlier term's month posts against THAT month only, and a retry with the same operation id replays it.
        var posting = new CollectionComposerWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(today));
        var operationId = Guid.NewGuid();
        var request = new MobileObligationPostRequest(operationId, open.Account.Id, 2023, 11, 400m, today);
        var posted = await posting.PostMobileObligationAsync(request);
        Assert.True(posted.IsSuccess, posted.Error);
        var replay = await posting.PostMobileObligationAsync(request);
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(posted.Value!.ReferenceCode, replay.Value!.ReferenceCode);
        Assert.Single(await db.Collections.ToListAsync());                                              // never twice

        var after = await ChoicesAsync();
        Assert.Equal(600m, after[earlierMonth].ServerAmount);                                           // the posted collection reduced only that month
        Assert.EndsWith("· Arrears, expired contract", after[earlierMonth].Context);                    // and it is still read as earlier-term Arrears
        Assert.Equal(before[earlierMonth].SelectionKey, after[earlierMonth].SelectionKey);
        Assert.Equal(1000m, after[currentTermMonth].ServerAmount);
        // The office's per-term balances moved by exactly that amount and no other.
        await using var office = database.CreateContext(w.TenantId);
        var account = (await new ObligationWorkflow(office, new Caller(w.CollectorId, w.TenantId, "SuperAdmin"), new Tenant(w.TenantId), new Clock(today))
            .GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!.Single();
        Assert.Equal((2, termsBefore[0].Outstanding - 400m, termsBefore[1].Outstanding), (account.Terms!.Count, account.Terms![0].Outstanding, account.Terms![1].Outstanding));
        Assert.Equal(account.OutstandingToDate, account.Terms.Sum(t => t.Outstanding));
    }

    [SkippableTheory]
    [InlineData(LotRentalEvent.Fiesta)]
    [InlineData(LotRentalEvent.Araw)]
    public async Task Future_fiesta_lot_is_visible_by_registered_payor_but_not_selectable_until_event_day(LotRentalEvent rentalEvent)
    {
        var today = PhilippineTime.Today;
        var eventDay = today.AddDays(7);
        var w = await SeedAsync(linked: false);
        await using var db = database.CreateContext(w.TenantId);
        var payor = await db.Payors.SingleAsync();
        var lot = ObligationAccount.Create(w.TenantId, ObligationKind.FiestaArawLotRental,
            payor.Id, null, "F-07", rentalEvent, eventDay, eventDay, "Head");
        db.Add(lot);
        db.Add(ObligationRate.Create(w.TenantId, lot.Id, eventDay, 850m, "Head"));
        // A closed past event may still have a Head-approved balance: collectors can accept late settlement.
        var past = today.AddDays(-14);
        var closed = ObligationAccount.Create(w.TenantId, ObligationKind.FiestaArawLotRental,
            payor.Id, null, "OLD-03", rentalEvent, past, past, "Head");
        closed.Close(past.AddDays(1));
        db.Add(closed);
        db.Add(ObligationRate.Create(w.TenantId, closed.Id, past, 450m, "Head"));
        var classification = RevenueClassification.Create(RevenueClassificationCodes.FiestaArawLotRental, w.TenantId);
        db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, past.AddYears(-1),
            "Event lot", RevenueInstrumentType.OfficialReceipt, w.TenantId));
        db.Add(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId,
            CollectorOperationCodes.FiestaArawLotRental, "Head"));
        await db.SaveChangesAsync();

        var sourceId = new CollectionSourceIdentity(SourceIdentityKind.SpaceAccount, lot.Id);
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId),
            new Tenant(w.TenantId), new Clock(today), null!, new NoMarketDays());
        var page = await sources.BrowseSourcesAsync(CollectionSourceCategories.FiestaAraw, null, 0, 25, default);
        Assert.Equal(2, page.Items.Count);
        var registered = Assert.Single(page.Items, x => x.Identity == sourceId);
        var overdue = Assert.Single(page.Items, x => x.Identity.Id == closed.Id);
        Assert.True(overdue.CanSelect);
        Assert.Null(overdue.AvailableOn);
        Assert.Equal(sourceId, registered.Identity);
        Assert.Equal(payor.DisplayName, registered.DisplayName);
        Assert.False(registered.CanSelect);
        Assert.Equal(eventDay, registered.AvailableOn);
        Assert.Equal(2, (await sources.BrowseSourcesAsync(CollectionSourceCategories.FiestaAraw,
            payor.DisplayName, 0, 25, default)).Items.Count);
        Assert.Contains(await sources.SearchSourcesAsync(payor.DisplayName, default),
            x => x.Identity == sourceId && !x.CanSelect && x.AvailableOn == eventDay);
        Assert.DoesNotContain((await sources.DiscoverNativeAsync(sourceId, today, default)).Operations,
            x => x.OperationCode == CollectorOperationCodes.FiestaArawLotRental);
        // Past event retains its original event period and collectible balance, not today's date.
        var late = await sources.DiscoverNativeAsync(new(SourceIdentityKind.SpaceAccount, closed.Id), today, default);
        Assert.Contains(late.Operations, x => x.OperationCode == CollectorOperationCodes.FiestaArawLotRental
            && x.EligibleChoiceCount > 0 && x.Choices!.Any(c => c.Identity?.PeriodStart == past));
        // The collector sees why it is owed: an ended occupancy's balance is Arrears, by the office's own rule. Display only — key and amount unchanged.
        var lateChoice = late.Operations.SelectMany(x => x.Choices ?? []).Single(c => c.Identity?.PeriodStart == past);
        Assert.EndsWith("· Arrears, ended occupancy", lateChoice.Context);
        Assert.Equal(450m, lateChoice.ServerAmount);
        Assert.Empty(await db.CollectionAllocations.ToListAsync());
        Assert.Empty(await db.Collections.ToListAsync());
        var latePosting = new CollectionComposerWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(today));
        var operationId = Guid.NewGuid();
        var payment = new MobileObligationPostRequest(operationId, closed.Id, past.Year, past.Month, 200m, today);
        var posted = await latePosting.PostMobileObligationAsync(payment);
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.NotNull(posted.Value);
        var replay = await latePosting.PostMobileObligationAsync(payment);
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(posted.Value!.ReferenceCode, replay.Value!.ReferenceCode);
        Assert.Single(await db.Collections.ToListAsync());
        var stillDue = Assert.Single((await sources.BrowseSourcesAsync(CollectionSourceCategories.FiestaAraw,
            null, 0, 25, default)).Items, x => x.Identity.Id == closed.Id);
        Assert.True(stillDue.CanSelect);
        var remainder = await latePosting.PostMobileObligationAsync(new(Guid.NewGuid(), closed.Id,
            past.Year, past.Month, 250m, today));
        Assert.True(remainder.IsSuccess, remainder.Error);
        Assert.Equal(2, await db.Collections.CountAsync());
        var paid = Assert.Single((await sources.BrowseSourcesAsync(CollectionSourceCategories.FiestaAraw,
            null, 0, 25, default)).Items, x => x.Identity.Id == closed.Id);
        Assert.False(paid.CanSelect); // never collect the same closed event again.

        // The same registered identity becomes selectable once the event actually occurs.
        var eventSources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId),
            new Tenant(w.TenantId), new Clock(eventDay), null!, new NoMarketDays());
        var onDay = Assert.Single((await eventSources.BrowseSourcesAsync(CollectionSourceCategories.FiestaAraw,
            null, 0, 25, default)).Items, x => x.Identity == sourceId);
        Assert.True(onDay.CanSelect);
        Assert.Null(onDay.AvailableOn);
        Assert.Contains((await eventSources.DiscoverNativeAsync(sourceId, eventDay, default)).Operations,
            x => x.OperationCode == CollectorOperationCodes.FiestaArawLotRental && x.EligibleChoiceCount > 0);

        // Authorization and tenancy still gate even purely informational registered rows.
        var differentTenant = new CollectionSessionSources(db, new Caller(w.CollectorId, Guid.NewGuid()),
            new Tenant(Guid.NewGuid()), new Clock(today), null!, new NoMarketDays());
        Assert.Empty((await differentTenant.BrowseSourcesAsync(CollectionSourceCategories.FiestaAraw,
            null, 0, 25, default)).Items);
        db.CollectorOperationAssignments.RemoveRange(db.CollectorOperationAssignments.Where(
            x => x.MunicipalityId == w.TenantId && x.CollectorId == w.CollectorId
                && x.OperationCode == CollectorOperationCodes.FiestaArawLotRental));
        await db.SaveChangesAsync();
        Assert.Empty((await sources.BrowseSourcesAsync(CollectionSourceCategories.FiestaAraw,
            null, 0, 25, default)).Items);
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

    [SkippableFact]
    public async Task Browsing_counts_only_the_occupancy_answering_this_month_a_successor_replaces_a_former_lessee_and_an_ended_one_is_not_listed()
    {
        var Today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false); await using var db = database.CreateContext(w.TenantId);
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(Today), null!, new NoMarketDays());
        var before = await sources.BrowseSourcesAsync(null, "one payer", 0, 25, default);
        var first = Assert.Single(before.Items);
        Assert.Equal(before.Items.Count, before.Total);

        // The lessee leaves before this month and a successor takes the stall: the former name no longer finds a source, the successor does.
        var contract = await db.Contracts.SingleAsync();
        contract.Terminate("test", new DateOnly(Today.Year, Today.Month, 1).AddDays(-1));
        var successor = Contract.Create(contract.StallId, "Successor Lessee", "Successor Lessee", new DateOnly(Today.Year, Today.Month, 1), 3, 900m, createdBy: "test");
        db.Add(successor); await db.SaveChangesAsync();

        var former = await sources.BrowseSourcesAsync(null, "one payer", 0, 25, default);
        Assert.Empty(former.Items); Assert.Equal(0, former.Total);                    // the total never counts a lessee who no longer answers
        var now = await sources.BrowseSourcesAsync(null, "successor", 0, 25, default);
        var row = Assert.Single(now.Items);
        Assert.Equal(1, now.Total);
        Assert.Equal(new CollectionSourceIdentity(SourceIdentityKind.Occupancy, successor.Id), row.Identity);
        Assert.NotEqual(first.Identity, row.Identity);                                // a different occupancy is a different source
    }

    [SkippableFact]
    public async Task Browsing_all_walks_each_category_in_its_own_order_with_no_gap_or_repeat_even_with_accents_and_punctuation()
    {
        var Today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var office = Office(db, w, "SuperAdmin");
        foreach (var name in new[] { "Álvaro Cruz", "-Dash Vendor", "O'Brien Fish", "alma reyes", "Zed Meat" })
            Assert.True((await office.RegisterAsync(new(Guid.NewGuid(), Today.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, name))).IsSuccess);
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(Today), null!, new NoMarketDays());

        var whole = await sources.BrowseSourcesAsync(null, null, 0, CollectionSourceCategories.MaxPage, default);
        var walked = new List<CollectionSourceSearchResult>();
        for (var skip = 0; skip < whole.Total; skip += 2)
            walked.AddRange((await sources.BrowseSourcesAsync(null, null, skip, 2, default)).Items);

        Assert.Equal(whole.Total, walked.Count);                                       // pages of two cover the whole list exactly
        Assert.Equal(walked.Count, walked.Select(x => x.Identity).Distinct().Count());   // and never repeat a source
        Assert.Equal(whole.Items.Select(x => x.Identity), walked.Select(x => x.Identity));
    }

    [SkippableFact]
    public async Task Browsing_finds_an_occupancy_by_its_stall_number_as_the_placeholder_promises()
    {
        var Today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false); await using var db = database.CreateContext(w.TenantId);
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(Today), null!, new NoMarketDays());
        var stallNo = (await db.Stalls.AsNoTracking().FirstAsync()).StallNo;

        var found = await sources.BrowseSourcesAsync(null, stallNo, 0, 25, default);

        Assert.Contains(found.Items, x => x.Identity.Kind == SourceIdentityKind.Occupancy && x.Context.EndsWith("Stall " + stallNo));
        Assert.Equal(found.Items.Count, found.Total);
    }
}
