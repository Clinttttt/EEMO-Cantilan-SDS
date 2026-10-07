using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Payments;
using System.Text.Json;

namespace EEMOCantilanSDS.IntegrationTests;
public sealed partial class CollectionSessionTests
{
    private static OfficeCollectionWorkflow Office(AppDbContext db, World w, string role = "Collector") =>
        new(db, new Caller(w.CollectorId, w.TenantId, role), new Tenant(w.TenantId), new Clock(PhilippineTime.Today), new FeeRateResolver(db));
    private static CollectionSessionWorkflow NativeWorkflow(AppDbContext db, World w, ICollectionSessionSources sources) =>
        new(new CollectionSessionStore(db), sources, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(PhilippineTime.Today), NullLogger<CollectionSessionWorkflow>.Instance);
    private static async Task EnableOffice(AppDbContext db, World w)
    {
        foreach (var operation in new[] { CollectorOperationCodes.Terminal, CollectorOperationCodes.FishMeatVendorFee, CollectorOperationCodes.WeightAndMeasure })
            db.Add(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, operation, "head"));
        foreach (var section in Enum.GetValues<TerminalSection>())
        {
            var c = RevenueClassification.Create(OfficeCollectionWorkflow.SectionCode(section), w.TenantId);
            db.AddRange(c, RevenueClassificationPolicy.Create(c.Id, OfficeCollectionWorkflow.EffectiveFrom,
                OfficeCollectionWorkflow.SectionName(section), RevenueInstrumentType.CashTicket, w.TenantId));
        }
        var weight = RevenueClassification.Create(RevenueClassificationCodes.WeightAndMeasure, w.TenantId);
        db.AddRange(weight, RevenueClassificationPolicy.Create(weight.Id, OfficeCollectionWorkflow.EffectiveFrom, "Weight & Measure", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 2m, OfficeCollectionWorkflow.EffectiveFrom, w.TenantId),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMeatPerKilo, 22m, OfficeCollectionWorkflow.EffectiveFrom, w.TenantId));
        await db.SaveChangesAsync();
    }
    [SkippableFact]
    public async Task Office_source_lists_fail_closed_without_matching_operation_assignment()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var office = Office(db, w);
        Assert.Equal(ResultStatus.Forbidden, (await office.RegistrationsAsync(date.Year)).Status);
        Assert.Empty(await office.VehicleChoicesAsync(date, default));
    }
    [SkippableTheory]
    [InlineData(TerminalSection.ComfortRoom)]
    [InlineData(TerminalSection.PullPulVansCargoVans)]
    [InlineData(TerminalSection.Tricycad)]
    public async Task Terminal_aggregate_is_its_own_CT_classification_and_ticket_count_never_prices_money(TerminalSection section)
    {
        var Today = PhilippineTime.Today;
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var request = new SourceNativeCollectionRequest(Guid.NewGuid(), Today, 5800m,
            new(CollectorOperationCodes.Terminal, Section: section, CashTicketCount: 7));
        var flow = Office(db, w); var quote = (await flow.QuoteAsync(request)).Value!;
        Assert.Equal(5800m, quote.Amount); Assert.Equal(RevenueInstrumentType.CashTicket, quote.Instrument);
        var posted = await flow.PostAsync(request); Assert.True(posted.IsSuccess, posted.Error);
        Assert.StartsWith("SRC-", posted.Value!.ReferenceCode);
        var replay = (await flow.PostAsync(request)).Value!; Assert.Equal(posted.Value.CollectionId, replay.CollectionId);
        Assert.Equal("CollectionIntentConflict", (await flow.PostAsync(request with { Charge = request.Charge with { CashTicketCount = 8 } })).Error);
        var line = await db.CollectionLines.SingleAsync();
        Assert.Equal(CollectionSourceKind.TerminalSection, line.SourceKind);
        Assert.Equal(OfficeCollectionWorkflow.SectionCode(section), (await db.RevenueClassifications.SingleAsync(x => x.Id == line.RevenueClassificationId)).SemanticCode);
        Assert.Empty(await db.TrmTrips.ToListAsync()); Assert.Single(await db.Collections.ToListAsync());
        var report = (await new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db),
            new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin"), new Tenant(w.TenantId), new Clock(Today)).Handle(new(Today.Year, Today.Month), default)).Value!;
        Assert.Equal(5800m, report.Groups.Single(x => x.Key == "TERMINAL").Total.Canonical);
        Assert.Equal(5800m, report.Sections!.Single(x => x.Key == "B").Total.Canonical);
        Assert.Equal(5800m, report.GrandTotal.Canonical);
        var collectorReport = await new CollectorReportQueries(db).GetCollectionsAsync(w.CollectorId, Today, Today);
        Assert.Equal(5800m, Assert.Single(collectorReport.OperationCollections!).Amount);
        var activity = await new CollectionActivityReader(db).GetAsync(w.TenantId, Today, Today);
        var activityRow = Assert.Single(activity.Where(x => x.CollectionId == posted.Value.CollectionId));
        Assert.Equal(5800m, activityRow.Amount);
        Assert.Equal(RevenueInstrumentType.CashTicket, activityRow.InstrumentType);
        var remit = (await new RemittanceWorkflow(db, new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin"), new Tenant(w.TenantId)).GetReviewAsync(new(Today, Today))).Value!;
        Assert.Equal(posted.Value.CollectionId, Assert.Single(remit.Collectors.SelectMany(x => x.Collections)).CollectionId);
    }
    [SkippableFact]
    public async Task Registered_meat_vendor_needs_no_NPM_or_master_and_native_checkout_keeps_fee_and_weighing_separate()
    {
        var Today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        db.RemoveRange(await db.ObligationRates.ToListAsync()); db.RemoveRange(await db.ObligationAccounts.ToListAsync()); db.RemoveRange(await db.Payors.ToListAsync());
        await db.SaveChangesAsync();
        var register = new RegisterFishMeatVendorRequest(Guid.NewGuid(), Today.Year, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Pantom Dant");
        var vendorResult = await Office(db, w, "SuperAdmin").RegisterAsync(register); Assert.True(vendorResult.IsSuccess, vendorResult.Error); var vendor = vendorResult.Value!;
        Assert.Equal(vendor.Id, (await Office(db, w, "SuperAdmin").RegisterAsync(register)).Value!.Id);
        Assert.Equal("RegistrationIntentConflict", (await Office(db, w, "SuperAdmin").RegisterAsync(register with { VendorType = FishMeatVendorType.Fish })).Error);
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(Today), null!, new NoMarketDays());
        var flow = NativeWorkflow(db, w, sources); var identity = new CollectionSourceIdentity(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id);
        Assert.Equal(identity, Assert.Single((await flow.SearchSourcesAsync("PANT")).Value!).Identity);
        var discovery = (await flow.DiscoverNativeAsync(identity)).Value!;
        Assert.Equal(2, discovery.Operations.Count(x => x.Family == CollectionFamily.Market));
        var choice = Assert.Single(discovery.Operations.Single(x => x.OperationCode == CollectorOperationCodes.WeightAndMeasure).Choices!);
        Assert.Equal(22m, choice.Rate); Assert.NotNull(choice.RateId); Assert.True(discovery.Operations.Where(x => x.Family == CollectionFamily.Market).All(x => x.CanAutoSelect));
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, null, [
            new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 100m, Native: new(CollectorOperationCodes.FishMeatVendorFee, vendor.Id)),
            new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 66m, Native: new(CollectorOperationCodes.WeightAndMeasure, vendor.Id, Kilograms: 3m))], SourceIdentity: identity);
        var posted = await Record(flow, intent); Assert.Equal(166m, posted.GrandTotal); Assert.Equal(2, posted.Collections.Count);
        Assert.Equal(2, posted.Collections.Select(x => x.ReferenceCode).Distinct().Count());
        var replay = (await flow.RecordAsync(new(intent, "response lost"))).Value!;
        Assert.Equal(posted.Collections.Select(x => x.CollectionId), replay.Collections.Select(x => x.CollectionId));
        Assert.Equal("SessionIntentConflict", Assert.Single((await flow.RecordAsync(new(intent with { PayerSnapshot = "changed" }, "response lost"))).Value!.Problems).Code);
        Assert.Empty(await db.DailyCollections.ToListAsync()); Assert.Empty(await db.ObligationPeriods.ToListAsync()); Assert.Empty(await db.Payors.ToListAsync());
        var lines = await db.CollectionLines.ToListAsync(); Assert.All(lines, x => Assert.Equal(vendor.Id, x.SourceId));
        var codes = await db.RevenueClassifications.ToDictionaryAsync(x => x.Id, x => x.SemanticCode);
        Assert.Equal(100m, lines.Single(x => codes[x.RevenueClassificationId] == RevenueClassificationCodes.FishMeatVendorFee).Amount);
        Assert.Contains("Kilograms", lines.Single(x => codes[x.RevenueClassificationId] == RevenueClassificationCodes.WeightAndMeasure).CalculationSnapshot);
        var official = (await new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db),
            new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin"), new Tenant(w.TenantId), new Clock(Today))
            .Handle(new(Today.Year, Today.Month), default)).Value!;
        var rows = official.Groups.SelectMany(x => x.Rows).ToDictionary(x => x.Key);
        Assert.Equal(100m, rows["FISH_MEAT_VENDOR_FEE"].Total.OfficialAmount);
        Assert.Equal(66m, rows["WEIGHT_AND_MEASURE"].Total.OfficialAmount);
        var collectorReport = await new CollectorReportQueries(db).GetCollectionsAsync(w.CollectorId, Today, Today);
        Assert.Equal(166m, collectorReport.OperationCollections!.Sum(x => x.Amount));
        Assert.Equal(2, collectorReport.OperationCollections!.Count);
    }
    [SkippableFact]
    public async Task Native_mixed_checkout_rolls_back_and_rate_staleness_tenant_and_type_fail_closed()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(linked: false);
        CollectionSessionIntent intent;
        await using (var db = database.CreateContext(w.TenantId))
        {
            await EnableOffice(db, w);
            var office = Office(db, w);
            var invalid = await Office(db, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), date.Year, (FishMeatVendorType)3, VendorRegistrationKind.New, "Invalid"));
            Assert.False(invalid.IsSuccess);
            var vendor = (await Office(db, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), date.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Independent vendor"))).Value!;
            var request = new SourceNativeCollectionRequest(Guid.NewGuid(), date, 6m, new(CollectorOperationCodes.WeightAndMeasure, vendor.Id, Kilograms: 3m));
            var quote = (await office.QuoteAsync(request)).Value!;
            var rate = await db.FacilityRates.SingleAsync(x => x.RateKey == FeeRateKey.NpmFishPerKilo);
            rate.UpdateAmount(3m, "head");
            await db.SaveChangesAsync();
            Assert.Equal("QuoteStale", (await office.PostAsync(request with { ExpectedSourceVersion = quote.SourceVersion })).Error);
            Assert.Equal("InvalidSource", (await office.QuoteAsync(request with { Charge = request.Charge with { VendorRegistrationId = Guid.NewGuid() } })).Error);
            Assert.Empty(await db.Collections.ToListAsync());
            var vehicle = VehicleClass.Create(w.TenantId, "TRICYCLE", "Tricycle", "head");
            Assert.Throws<ArgumentException>(() => vehicle.AssociateTerminalSection(TerminalSection.PullPulVansCargoVans));
            intent = new(Guid.NewGuid(), date, null, [
                new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 100m, Native: new(CollectorOperationCodes.FishMeatVendorFee, vendor.Id)),
                new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 9m, Native: new(CollectorOperationCodes.WeightAndMeasure, vendor.Id, Kilograms: 3m))],
                SourceIdentity: new(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id));
            var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(date), null!, new NoMarketDays());
            var flow = NativeWorkflow(db, w, new FailAfterFirst(sources));
            var reviewed = (await flow.QuoteAsync(intent)).Value!;
            Assert.True(reviewed.CanRecord, string.Join("; ", reviewed.Problems.Select(x => $"{x.Code}: {x.Message}")));
            Assert.Single(reviewed.InstrumentTotals);
            await Assert.ThrowsAsync<IOException>(() => flow.RecordAsync(new(intent, reviewed.QuoteFingerprint)));
        }
        await using var verify = database.CreateContext(w.TenantId);
        Assert.Empty(await verify.Collections.ToListAsync());
        Assert.Empty(await verify.MobileCollectionSessions.ToListAsync());
        var actualSources = new CollectionSessionSources(verify, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(date), null!, new NoMarketDays());
        var outcome = await Record(NativeWorkflow(verify, w, actualSources), intent);
        Assert.Equal(109m, outcome.GrandTotal); Assert.Equal(2, outcome.Collections.Count);
        Assert.Equal(2, outcome.Collections.Select(x => x.ReferenceCode).Distinct().Count());
    }
    [SkippableFact]
    public async Task A_selected_source_is_offered_only_its_own_items_and_direct_operations_belong_to_a_source_less_session()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(linked: false);
        await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var vendor = (await Office(db, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), date.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Independent vendor"))).Value!;
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(date), null!, new NoMarketDays());

        var fish = await sources.DiscoverNativeAsync(new(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id), date, default);
        Assert.Equal([CollectorOperationCodes.FishMeatVendorFee, CollectorOperationCodes.WeightAndMeasure],
            fish.Operations.Select(x => x.OperationCode).OrderBy(x => x, StringComparer.Ordinal).ToArray());      // nothing else, however payer-optional

        var occupancy = (await db.Contracts.SingleAsync()).Id;
        var npm = await sources.DiscoverNativeAsync(new(SourceIdentityKind.Occupancy, occupancy), date, default);
        Assert.DoesNotContain(npm.Operations, x => x.OperationCode is CollectorOperationCodes.Terminal or CollectorOperationCodes.Transportation
            or CollectorOperationCodes.FishMeatVendorFee or CollectorOperationCodes.WeightAndMeasure);

        // A source with nothing collectible is empty; walk-up work is a separate direct session with no Source Identity.
        var direct = await sources.DiscoverNativeAsync(null, date, default);
        Assert.Contains(direct.Operations, x => x.OperationCode == CollectorOperationCodes.Terminal);
        Assert.Contains(direct.Operations, x => x.OperationCode == CollectorOperationCodes.FishMeatVendorFee);
        Assert.DoesNotContain(direct.Operations, x => x.OperationCode == CollectorOperationCodes.WeightAndMeasure);   // a weighing needs a registered vendor
    }
    [SkippableFact]
    public async Task A_source_bound_session_rejects_an_unrelated_direct_item_but_a_direct_session_accepts_it()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(linked: false);
        await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var vendor = (await Office(db, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), date.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Independent vendor"))).Value!;
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(date), null!, new NoMarketDays());
        var flow = NativeWorkflow(db, w, sources);
        CollectionSessionItemIntent Terminal() => new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 80m,
            Native: new(CollectorOperationCodes.Terminal, Section: TerminalSection.ComfortRoom));

        var crafted = (await flow.QuoteAsync(new(Guid.NewGuid(), date, null, [Terminal()],
            SourceIdentity: new(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id)))).Value!;
        Assert.False(crafted.CanRecord);
        Assert.Contains(crafted.Problems, x => x.Code == "PayerMismatch");
        Assert.Empty(await db.Collections.ToListAsync());

        var outcome = await Record(flow, new(Guid.NewGuid(), date, null, [Terminal()], PayerSnapshot: "Walk-up payer"));
        Assert.Equal(80m, outcome.GrandTotal); Assert.Single(outcome.Collections);
    }
    [SkippableFact]
    public async Task Terminal_resolves_exact_known_vehicle_codes_without_re_mapping_and_leaves_custom_classes_unmapped()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        VehicleClass Add(string code) { var v = VehicleClass.Create(w.TenantId, code, code, "head"); db.AddRange(v, VehicleClassRate.Create(w.TenantId, v.Id, date, 20m, "head")); return v; }
        var jeepney = Add("JEEPNEY"); var tricycle = Add("TRICYCLE"); var custom = Add("PEDICAB");
        await db.SaveChangesAsync();
        Assert.Null(jeepney.TerminalSection);                                                                    // no explicit mapping was ever saved

        var choices = await Office(db, w).VehicleChoicesAsync(date, default);
        Assert.Equal(TerminalSection.PullPulVansCargoVans, choices.Single(x => x.VehicleClassId == jeepney.Id).Section);
        Assert.Equal(TerminalSection.Tricycad, choices.Single(x => x.VehicleClassId == tricycle.Id).Section);
        Assert.DoesNotContain(choices, x => x.VehicleClassId == custom.Id);                                      // unknown/custom: only the Head can map it

        Assert.True((await Office(db, w, "SuperAdmin").MapVehicleAsync(new(custom.Id, TerminalSection.Tricycad))).IsSuccess);
        Assert.Contains(await Office(db, w).VehicleChoicesAsync(date, default), x => x.VehicleClassId == custom.Id);
        Assert.Empty(await db.Collections.ToListAsync());                                                        // configuration interpretation only: no money moved
    }
    [SkippableFact]
    public async Task Unregistered_vendor_fee_preserves_snapshot_but_cannot_weigh_or_create_identity()
    {
        var Today = PhilippineTime.Today;
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var flow = Office(db, w);
        var request = new SourceNativeCollectionRequest(Guid.NewGuid(), Today, 50m, new(CollectorOperationCodes.FishMeatVendorFee), "One Payer");
        Assert.True((await flow.PostAsync(request)).IsSuccess);
        Assert.True((await flow.PostAsync(request with { ClientOperationId = Guid.NewGuid(), AmountReceived = 30m })).IsSuccess);
        Assert.Equal("One Payer", (await db.Collections.FirstAsync()).PayerName); Assert.Null((await db.Collections.FirstAsync()).PayorId);
        Assert.Empty(await db.FishMeatVendorRegistrations.ToListAsync()); Assert.Single(await db.Payors.ToListAsync());
        Assert.All(await db.CollectionLines.ToListAsync(), x => { Assert.Null(x.SourceKind); Assert.Null(x.SourceId); });
        Assert.Equal("RequiresRegisteredVendor", (await flow.QuoteAsync(request with { Charge = new(CollectorOperationCodes.WeightAndMeasure, Kilograms: 3m) })).Error);
        Assert.Equal(80m, await db.Collections.SumAsync(x => x.TotalAmount)); Assert.Empty(await db.DailyCollections.ToListAsync());
    }
    [SkippableFact]
    public async Task Source_native_occupancy_discovery_does_not_require_a_master_link_or_merge_same_names()
    {
        var Today = PhilippineTime.Today;
        var w = await SeedAsync(linked: false); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var registered = (await Office(db, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), Today.Year, FishMeatVendorType.Fish, VendorRegistrationKind.Renew, "One Payer"))).Value!;
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(Today), null!, new NoMarketDays());
        var found = await sources.SearchSourcesAsync("one", default);
        Assert.Contains(found, x => x.Identity.Kind == SourceIdentityKind.Occupancy);
        Assert.Contains(found, x => x.Identity == new CollectionSourceIdentity(SourceIdentityKind.FishMeatVendorRegistration, registered.Id));
        Assert.Equal(2, found.Count(x => x.DisplayName == "One Payer")); Assert.Null((await db.Contracts.SingleAsync()).PayorId);
        var other = new CollectionSessionSources(db, new Caller(w.CollectorId, Guid.NewGuid()), new Tenant(Guid.NewGuid()), new Clock(), null!, new NoMarketDays());
        Assert.Empty(await other.SearchSourcesAsync("one", default));
    }
    [SkippableTheory]
    [InlineData("TRICYCLE", TerminalSection.Tricycad)]
    [InlineData("JEEPNEY", TerminalSection.PullPulVansCargoVans)]
    [InlineData("MULTICAB", TerminalSection.PullPulVansCargoVans)]
    [InlineData("VAN", TerminalSection.PullPulVansCargoVans)]
    [InlineData("PUBLIC_UTILITY_BUS", TerminalSection.PullPulVansCargoVans)]
    [InlineData("PUBLIC_UTILITY_BABY_BUS", TerminalSection.PullPulVansCargoVans)]
    public async Task Terminal_reuses_explicit_class_mapping_and_freezes_rate_aid_without_pricing_aggregate(string code, TerminalSection section)
    {
        var date = PhilippineTime.Today;
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var vehicle = VehicleClass.Create(w.TenantId, code, code, "head");
        var firstRate = VehicleClassRate.Create(w.TenantId, vehicle.Id, date, 25m, "head");
        db.AddRange(vehicle, firstRate); await db.SaveChangesAsync();
        Assert.Equal(ResultStatus.Forbidden, (await Office(db, w).MapVehicleAsync(new(vehicle.Id, section))).Status);
        Assert.True((await Office(db, w, "SuperAdmin").MapVehicleAsync(new(vehicle.Id, section))).IsSuccess);
        var flow = Office(db, w);
        Assert.Equal(section, Assert.Single(await flow.VehicleChoicesAsync(date, default)).Section);
        var request = new SourceNativeCollectionRequest(Guid.NewGuid(), date, 5800m, new(CollectorOperationCodes.Terminal, Section: section, VehicleClassId: vehicle.Id));
        var quote = (await flow.QuoteAsync(request)).Value!;
        Assert.Equal(25m, quote.Rate); Assert.Equal(5800m, quote.Amount);
        var posted = await flow.PostAsync(request with { ExpectedSourceVersion = quote.SourceVersion }); Assert.True(posted.IsSuccess, posted.Error);
        db.Add(VehicleClassRate.Create(w.TenantId, vehicle.Id, date.AddDays(1), 40m, "head")); await db.SaveChangesAsync();
        Assert.Equal(40m, Assert.Single(await flow.VehicleChoicesAsync(date.AddDays(1), default)).Rate);
        var activity = Assert.Single((await Office(db, w, "SuperAdmin").ActivityAsync(date, date)).Value!);
        Assert.Equal((5800m, 25m, firstRate.Id, vehicle.Id), (activity.Amount, activity.Rate, activity.RateId, activity.VehicleClassId));
        Assert.Equal(posted.Value!.ReferenceCode, activity.SRC); Assert.Equal(section, activity.Section);
        Assert.Empty(await db.TrmTrips.ToListAsync());
    }
    [SkippableFact]
    public async Task Prospective_Transportation_is_direct_without_vehicle_rate_or_ceiling_and_legacy_class_evidence_is_not_reclassified()
    {
        var date = PhilippineTime.Today;
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var collector = new Caller(w.CollectorId, w.TenantId); var tenant = new Tenant(w.TenantId); var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var classification = RevenueClassification.Create(RevenueClassificationCodes.TransportationParking, w.TenantId);
        db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, date.AddDays(-1), "Transportation / Parking", RevenueInstrumentType.CashTicket, w.TenantId),
            CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Transportation, "head")); await db.SaveChangesAsync();
        var setup = new GovernedServiceWorkflow(db, head, tenant, new Clock(date));
        Assert.True((await setup.ConfigureAsync(CollectorOperationCodes.Transportation, new(date, GovernedServiceBasis.DirectApprovedAmount, null, 10m, true, true))).IsSuccess);
        var flow = new GovernedServiceWorkflow(db, collector, tenant, new Clock(date));
        var terms = (await flow.GetTermsAsync(CollectorOperationCodes.Transportation, null)).Value!;
        Assert.Null(terms.MaximumAmount); Assert.Empty(terms.VehicleClasses ?? []);
        var request = new GovernedServicePostRequest(1, Guid.NewGuid(), CollectorOperationCodes.Transportation, date, 5800m, null, null, null);
        var posted = await flow.PostMobileAsync(request); Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(RevenueInstrumentType.CashTicket, posted.Value!.Instrument);
        Assert.Equal(posted.Value.ReferenceCode, (await flow.PostMobileAsync(request)).Value!.ReferenceCode);
        Assert.Empty(await db.VehicleClasses.ToListAsync()); Assert.Empty(await db.TrmTrips.ToListAsync());
        Assert.Equal(RevenueClassificationCodes.TransportationParking, (await db.RevenueClassifications.SingleAsync(x => x.Id == (db.CollectionLines.Single()).RevenueClassificationId)).SemanticCode);
        Assert.Empty((await Office(db, w, "SuperAdmin").ActivityAsync(date, date, CollectorOperationCodes.Terminal)).Value!);
    }
    [SkippableFact]
    public async Task Terminal_adjustment_targets_and_signatories_are_official_only_and_Head_only()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var post = (await Office(db, w).PostAsync(new(Guid.NewGuid(), date, 5800m, new(CollectorOperationCodes.Terminal, Section: TerminalSection.ComfortRoom)))).Value!;
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin"); var tenant = new Tenant(w.TenantId); var clock = new Clock(date);
        var rowKey = RevenueClassificationCodes.TerminalComfortRoom;
        var request = new SetMonthlyIncomeAdjustmentRequest(Guid.NewGuid(), rowKey, date.Year, date.Month, 5800m, 5900m, "Signed office report", "Office evidence");
        foreach (var role in new[] { "Admin", "Collector" })
            Assert.Equal(ResultStatus.Forbidden, (await new ReportGovernanceWorkflow(db, new Caller(w.CollectorId, w.TenantId, role), tenant, clock, new LegacyMonthlyIncomeReader(db)).AdjustAsync(request)).Status);
        var governance = new ReportGovernanceWorkflow(db, head, tenant, clock, new LegacyMonthlyIncomeReader(db));
        Assert.True((await governance.AdjustAsync(request)).IsSuccess);
        Assert.True((await governance.SetTargetAsync(new(Guid.NewGuid(), rowKey, date.Year, 10000m, "Approved target"))).IsSuccess);
        var municipality = await db.Municipalities.SingleAsync(x => x.Id == w.TenantId);
        municipality.SetReportSignatories(JsonSerializer.Serialize(new { Lines = new[] { new { Caption = "Prepared by", Name = "Office preparer", Title = "Revenue Officer" }, new { Caption = "Certified Correct", Name = "Office certifier", Title = "MEEDO Head" } } }), "head"); await db.SaveChangesAsync();
        var report = (await new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), head, tenant, clock).Handle(new(date.Year, date.Month), default)).Value!;
        var row = report.Groups.SelectMany(g => g.Rows).Single(x => x.Key == rowKey);
        Assert.Equal((5800m, 100m, 5900m), (row.Total.SystemAmount, row.Total.AdjustmentAmount, row.Total.OfficialAmount));
        Assert.Equal(59m, row.Attainment); Assert.Equal("MEEDO Head", report.Signatories![1].Title);
        Assert.Equal(5800m, (await db.Collections.SingleAsync()).TotalAmount); Assert.Equal(5800m, (await db.CollectionLines.SingleAsync()).Amount);
        Assert.Equal(post.ReferenceCode, (await db.Collections.SingleAsync()).ReferenceCode);
        var remittance = (await new RemittanceWorkflow(db, head, tenant).GetReviewAsync(new(date, date))).Value!;
        Assert.Equal(5800m, remittance.Collectors.SelectMany(x => x.Collections).Sum(x => x.NetAmount));
        Assert.Empty(await db.ObligationPeriods.ToListAsync()); Assert.Empty(await db.CollectionRemittances.ToListAsync());
    }
    [SkippableFact]
    public async Task Historical_unresolved_weighing_is_not_repriced_and_activity_agrees_with_official_income()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var daily = DailyCollection.Create(w.StallId, date, "legacy", 30m);
        daily.MarkPaid("Historical OR", w.CollectorId, fishKilos: 3m); db.Add(daily); await db.SaveChangesAsync();
        Assert.Null(daily.FishFeeAmountFrozen);
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        async Task AssertUnchanged()
        {
            var report = (await new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), head, new Tenant(w.TenantId), new Clock(date)).Handle(new(date.Year, date.Month), default)).Value!;
            Assert.Equal(30m, report.GrandTotal.SystemAmount);
            var activity = await new CollectionActivityReader(db).GetAsync(w.TenantId, date, date);
            Assert.Equal(30m, activity.Sum(x => x.Amount));
            Assert.Equal(30m, (await new CollectorReportQueries(db).GetCollectionsAsync(w.CollectorId, date, date)).Lines.Sum(x => x.Amount));
        }
        await AssertUnchanged();
        (await db.FacilityRates.SingleAsync(x => x.RateKey == FeeRateKey.NpmFishPerKilo)).UpdateAmount(99m); await db.SaveChangesAsync();
        await AssertUnchanged(); Assert.Null((await db.DailyCollections.SingleAsync()).FishFeeAmountFrozen);
    }
    [SkippableFact]
    public async Task Source_owned_space_holder_can_be_created_discovered_and_collected_without_a_master()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.KanmanggaySpaceRental, w.TenantId);
        db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, date, "Kanmanggay", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.KanmanggaySpaceRental, "head")); await db.SaveChangesAsync();
        var office = new ObligationWorkflow(db, new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin"), new Tenant(w.TenantId), new Clock(date));
        var request = new CreateObligationAccountRequest(ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "", null, null, date, 200m,
            OccupancyArrangement.SpaceOnly, ActualOccupant: "Source holder without a master");
        var created = await office.CreateAccountAsync(request); Assert.True(created.IsSuccess, created.Error);
        Assert.Equal("1", created.Value!.SubjectLabel); Assert.Equal(request.ActualOccupant, created.Value.PayerName);
        var account = await db.ObligationAccounts.SingleAsync(x => x.Id == created.Value.Id); Assert.Null(account.PayorId);
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(date), null!, new NoMarketDays());
        var identity = new CollectionSourceIdentity(SourceIdentityKind.SpaceAccount, account.Id);
        Assert.Equal(identity, Assert.Single(await sources.SearchSourcesAsync("without", default)).Identity);
        var flow = NativeWorkflow(db, w, sources);
        var discovery = (await flow.DiscoverNativeAsync(identity)).Value!;
        Assert.True(Assert.Single(discovery.Operations).CanAutoSelect);
        var intent = new CollectionSessionIntent(Guid.NewGuid(), date, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Obligation, 200m, Obligation: new(account.Id, date.Year, date.Month))], SourceIdentity: identity);
        var posted = await Record(flow, intent); Assert.Equal(200m, posted.GrandTotal);
        Assert.Null((await db.Collections.SingleAsync()).PayorId); Assert.Equal(request.ActualOccupant, (await db.Collections.SingleAsync()).PayerName);
        Assert.Single(await db.Payors.ToListAsync()); Assert.Equal(0m, Assert.Single((await office.GetRegisterAsync(account.Id)).Value!).OutstandingAmount);
        Assert.Equal(posted.Collections[0].ReferenceCode, (await flow.RecordAsync(new(intent, "unknown response"))).Value!.Collections[0].ReferenceCode);
        var preview = (await office.PreviewSpaceHoldersAsync(new([new(request with { ActualOccupant = null })]))).Value!;
        Assert.Equal(SpaceHolderImportStatus.NeedsSourceHolder, Assert.Single(preview.Rows).Status);
        Assert.Equal(SpaceHolderImportAction.ConfirmSourceHolder, preview.Rows[0].RequiredAction);
        var ready = (await office.PreviewSpaceHoldersAsync(new([new(request with { ActualOccupant = "Another holder" })]))).Value!;
        Assert.True(ready.CanSave); Assert.Equal(SpaceHolderImportStatus.Ready, ready.Rows[0].Status);
        Assert.Single((await office.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!);
        var saved = (await office.ImportSpaceHoldersAsync(new([new(request with { ActualOccupant = "Another holder" })]))).Value!;
        Assert.Equal(1, saved.Imported); Assert.Equal("2", saved.Rows![0].Facts!.Account.SubjectLabel);
    }
}
