using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;
public sealed partial class CollectionSessionTests
{
    [SkippableTheory]
    [InlineData(FishMeatVendorType.Fish)]
    [InlineData(FishMeatVendorType.Meat)]
    public async Task Registry_active_sources_offer_both_operations_and_closed_sources_preserve_history_but_cannot_collect(FishMeatVendorType type)
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var head = Office(db, w, "SuperAdmin");
        var registration = new RegisterFishMeatVendorRequest(Guid.NewGuid(), date.Year, type, VendorRegistrationKind.New, "Independent vendor");
        var vendor = (await head.RegisterAsync(registration)).Value!;
        var identity = new CollectionSourceIdentity(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id);
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(date), null!, new NoMarketDays());
        var eligible = await sources.DiscoverNativeAsync(identity, date, default);
        Assert.Contains(eligible.Operations, x => x.OperationCode == CollectorOperationCodes.FishMeatVendorFee);
        Assert.Contains(eligible.Operations, x => x.OperationCode == CollectorOperationCodes.WeightAndMeasure);
        var charge = new SourceNativeChargeIntent(CollectorOperationCodes.WeightAndMeasure, vendor.Id, Kilograms: 3m);
        var quote = (await Office(db, w).QuoteAsync(new(Guid.NewGuid(), date, 0m, charge))).Value!;
        Assert.Equal(EEMOCantilanSDS.Domain.Enums.RevenueInstrumentType.OfficialReceipt, quote.Instrument);
        Assert.Equal(type, quote.VendorType); Assert.Equal(3m * quote.Rate!.Value, quote.Amount);
        var intent = new SourceNativeCollectionRequest(Guid.NewGuid(), date, quote.Amount, charge);
        var posted = (await Office(db, w).PostAsync(intent)).Value!;
        var session = new CollectionSessionIntent(Guid.NewGuid(), date, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, quote.Amount, Native: charge)], SourceIdentity: identity);
        var flow = NativeWorkflow(db, w, sources);
        var sessionQuote = (await flow.QuoteAsync(session)).Value!; Assert.True(sessionQuote.CanRecord);
        var before = Assert.Single((await head.ActivityAsync(date, date)).Value!);
        var close = new CloseVendorRegistrationRequest(Guid.NewGuid(), " reviewed ");
        var result = (await head.CloseRegistrationAsync(vendor.Id, close)).Value!;
        Assert.Equal(VendorRegistrationStatus.Closed, result.Status); Assert.Equal(date, result.ClosedOn); Assert.NotNull(result.ClosedAtUtc);
        var replay = (await head.CloseRegistrationAsync(vendor.Id, close)).Value!;
        Assert.True(replay.ExistingOutcome); Assert.Equal(result.ClosedAtUtc, replay.ClosedAtUtc);
        Assert.Equal("RegistrationIntentConflict", (await head.CloseRegistrationAsync(vendor.Id, close with { Note = "changed" })).Error);
        Assert.Equal("RegistrationClosed", (await head.CloseRegistrationAsync(vendor.Id, close with { ClientOperationId = Guid.NewGuid() })).Error);
        Assert.Equal("RegistrationClosed", (await Office(db, w).QuoteAsync(intent with { ClientOperationId = Guid.NewGuid() })).Error);
        Assert.Equal("RegistrationClosed", (await Office(db, w).PostAsync(intent with { ClientOperationId = Guid.NewGuid() })).Error);
        Assert.Equal(posted.CollectionId, (await Office(db, w).PostAsync(intent)).Value!.CollectionId);
        Assert.Equal(CollectionSessionStatus.NeedsReview, (await flow.RecordAsync(new(session, sessionQuote.QuoteFingerprint))).Value!.Status);
        Assert.Empty((await sources.DiscoverNativeAsync(identity, date, default)).Operations);
        Assert.DoesNotContain(await sources.SearchSourcesAsync("Independent", default), x => x.Identity == identity);
        Assert.Empty((await Office(db, w).RegistrationsAsync(date.Year)).Value!);
        Assert.Single((await head.RegistrationsAsync(date.Year)).Value!);
        var managed = Assert.Single((await head.ManageRegistrationsAsync(date.Year, date.Month, VendorRegistrationStatus.Closed)).Value!.Rows);
        Assert.Equal(vendor.Id, managed.Registration.Id); Assert.Equal(quote.Amount, managed.WeightMeasureCollected);
        Assert.Equal(before, Assert.Single((await head.ActivityAsync(date, date)).Value!)); Assert.Single(await db.Collections.ToListAsync());
        // Closed identity never falls through to walk-up; only an explicitly unregistered Vendor Fee may use a snapshot.
        Assert.Contains((await sources.DiscoverNativeAsync(null, date, default)).Operations, x => x.OperationCode == CollectorOperationCodes.FishMeatVendorFee);
        Assert.DoesNotContain((await sources.DiscoverNativeAsync(null, date, default)).Operations, x => x.OperationCode == CollectorOperationCodes.WeightAndMeasure);
        Assert.Equal("RequiresRegisteredVendor", (await Office(db, w).QuoteAsync(new(Guid.NewGuid(), date, 0m,
            new(CollectorOperationCodes.WeightAndMeasure, Kilograms: 3m), "Independent vendor"))).Error);
        Assert.Equal(vendor.Id, (await head.RegisterAsync(registration)).Value!.Id); // registration replay is unaffected by closure
    }
    [SkippableFact]
    public async Task Registry_renewal_is_explicit_new_annual_identity_never_same_name_inference()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin");
        var first = (await head.RegisterAsync(new(Guid.NewGuid(), date.Year - 1, FishMeatVendorType.Meat, VendorRegistrationKind.New,
            "Same name", "Business", "Address", "Reference"))).Value!;
        var second = (await head.RegisterAsync(new(Guid.NewGuid(), date.Year - 1, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Same name"))).Value!;
        await head.CloseRegistrationAsync(first.Id, new(Guid.NewGuid()));
        var original = await db.FishMeatVendorRegistrations.AsNoTracking().SingleAsync(x => x.Id == first.Id);
        var request = new RenewVendorRegistrationRequest(Guid.NewGuid(), date.Year);
        var renewed = (await head.RenewRegistrationAsync(first.Id, request)).Value!;
        Assert.NotEqual(first.Id, renewed.Registration.Id); Assert.Equal(first.Id, renewed.PriorRegistrationId);
        Assert.Equal(VendorRegistrationKind.Renew, renewed.Registration.RegistrationKind); Assert.Equal(FishMeatVendorType.Meat, renewed.Registration.VendorType);
        Assert.Equal("Business", renewed.Registration.BusinessName); Assert.Equal(VendorRegistrationStatus.Active, renewed.Status);
        Assert.Equal(renewed.Registration.Id, (await head.RenewRegistrationAsync(first.Id, request)).Value!.Registration.Id);
        Assert.True((await head.RenewRegistrationAsync(first.Id, request)).Value!.ExistingOutcome);
        Assert.Equal("RegistrationIntentConflict", (await head.RenewRegistrationAsync(first.Id, request with { DisplayName = "changed" })).Error);
        Assert.Equal("RegistrationIntentConflict", (await head.RenewRegistrationAsync(second.Id, request)).Error);
        Assert.Equal("RenewalAlreadyExists", (await head.RenewRegistrationAsync(first.Id, request with { ClientOperationId = Guid.NewGuid() })).Error);
        Assert.Equal("InvalidRenewalYear", (await head.RenewRegistrationAsync(first.Id, new(Guid.NewGuid(), first.TaxYear))).Error);
        var separate = (await head.RenewRegistrationAsync(second.Id, new(Guid.NewGuid(), date.Year))).Value!;
        Assert.NotEqual(renewed.Registration.Id, separate.Registration.Id); Assert.Equal(second.Id, separate.PriorRegistrationId);
        var unchanged = await db.FishMeatVendorRegistrations.AsNoTracking().SingleAsync(x => x.Id == first.Id);
        Assert.Equal(original.ClientOperationId, unchanged.ClientOperationId); Assert.Equal(original.TaxYear, unchanged.TaxYear);
        Assert.Equal(original.ClosedAtUtc, unchanged.ClosedAtUtc); Assert.Equal(VendorRegistrationStatus.Closed, unchanged.Status);
        Assert.Empty(await db.Collections.ToListAsync());
    }
    [SkippableFact]
    public async Task Registry_monthly_money_is_canonical_correction_aware_and_never_attributed_by_name()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var head = Office(db, w, "SuperAdmin");
        var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), date.Year, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Same name"))).Value!;
        var another = (await head.RegisterAsync(new(Guid.NewGuid(), date.Year, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Same name"))).Value!;
        await Office(db, w).PostAsync(new(Guid.NewGuid(), date, 100m, new(CollectorOperationCodes.FishMeatVendorFee, vendor.Id)));
        var charge = new SourceNativeChargeIntent(CollectorOperationCodes.WeightAndMeasure, vendor.Id, Kilograms: 3m);
        var quote = (await Office(db, w).QuoteAsync(new(Guid.NewGuid(), date, 0m, charge))).Value!;
        var weight = (await Office(db, w).PostAsync(new(Guid.NewGuid(), date, quote.Amount, charge))).Value!;
        await Office(db, w).PostAsync(new(Guid.NewGuid(), date, 500m, new(CollectorOperationCodes.FishMeatVendorFee), "Same name"));
        var summary = (await head.ManageRegistrationsAsync(date.Year, date.Month)).Value!;
        var row = summary.Rows.Single(x => x.Registration.Id == vendor.Id);
        Assert.Equal(100m, row.VendorFeeCollected); Assert.Equal(quote.Amount, row.WeightMeasureCollected); Assert.Equal(100m + quote.Amount, row.TotalCollected);
        Assert.Equal(0m, summary.Rows.Single(x => x.Registration.Id == another.Id).TotalCollected); Assert.Equal(500m, summary.WalkUpVendorFeeCollected);
        var removed = await Corrections(db, w).RemoveAsync(new(Guid.NewGuid(), weight.CollectionId, new(MobileCorrectionReason.EnteredByMistake)));
        Assert.True(removed.IsSuccess);
        var corrected = (await head.ManageRegistrationsAsync(date.Year, date.Month)).Value!.Rows.Single(x => x.Registration.Id == vendor.Id);
        Assert.Equal(100m, corrected.TotalCollected); Assert.Equal(0m, corrected.WeightMeasureCollected); Assert.Equal(3, await db.Collections.CountAsync());
        var report = new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin"), new Tenant(w.TenantId), new Clock(date));
        var beforeClose = (await report.Handle(new(date.Year, date.Month), default)).Value!.GrandTotal.Total;
        await head.CloseRegistrationAsync(vendor.Id, new(Guid.NewGuid()));
        Assert.Equal(beforeClose, (await report.Handle(new(date.Year, date.Month), default)).Value!.GrandTotal.Total);
        Assert.Equal(100m, (await head.ManageRegistrationsAsync(date.Year, date.Month, VendorRegistrationStatus.Closed)).Value!.Rows.Single().TotalCollected);
        // Another active source with identical text never replaces the explicitly closed identity.
        Assert.Equal("RegistrationClosed", (await Office(db, w).QuoteAsync(new(Guid.NewGuid(), date, 0m, charge))).Error);
        var otherMonth = date.Month == 1 ? 2 : 1;
        Assert.All((await head.ManageRegistrationsAsync(date.Year, otherMonth)).Value!.Rows, x => Assert.Equal(0m, x.TotalCollected));
    }
    [SkippableFact]
    public async Task Registry_management_roles_and_tenant_boundaries_fail_closed()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin"); var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), date.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Private"))).Value!;
        Assert.True((await Office(db, w, "Admin").ManageRegistrationsAsync(date.Year, date.Month)).IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, (await Office(db, w).ManageRegistrationsAsync(date.Year, date.Month)).Status);
        foreach (var role in new[] { "Admin", "Collector" })
        {
            Assert.Equal(ResultStatus.Forbidden, (await Office(db, w, role).CloseRegistrationAsync(vendor.Id, new(Guid.NewGuid()))).Status);
            Assert.Equal(ResultStatus.Forbidden, (await Office(db, w, role).RenewRegistrationAsync(vendor.Id, new(Guid.NewGuid(), date.Year + 1))).Status);
        }
        var other = w with { TenantId = Guid.NewGuid() };
        await using var otherDb = database.CreateContext(other.TenantId);
        var outsider = Office(otherDb, other, "SuperAdmin");
        Assert.Empty((await outsider.ManageRegistrationsAsync(date.Year, date.Month)).Value!.Rows);
        Assert.Equal(ResultStatus.NotFound, (await outsider.CloseRegistrationAsync(vendor.Id, new(Guid.NewGuid()))).Status);
        Assert.Equal(ResultStatus.NotFound, (await outsider.RenewRegistrationAsync(vendor.Id, new(Guid.NewGuid(), date.Year + 1))).Status);
        Assert.Equal(ResultStatus.Invalid, (await head.ManageRegistrationsAsync(date.Year, 13)).Status);
        Assert.Equal(ResultStatus.Invalid, (await head.ManageRegistrationsAsync(date.Year, date.Month, (VendorRegistrationStatus)9)).Status);
        Assert.Equal(VendorRegistrationStatus.Active, (await db.FishMeatVendorRegistrations.AsNoTracking().SingleAsync()).Status);
    }
    [SkippableFact]
    public async Task Registry_import_replay_after_close_keeps_history_and_creates_no_money()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin");
        var request = new VendorRegistryImportRequest([new(1, new(Guid.NewGuid(), date.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Imported"))]);
        Assert.True((await head.PreviewImportAsync(request)).Value!.CanSave);
        Assert.Empty(await db.FishMeatVendorRegistrations.ToListAsync());
        var first = (await head.SaveImportAsync(request)).Value!.Rows.Single().Registration;
        await head.CloseRegistrationAsync(first.Id, new(Guid.NewGuid()));
        Assert.Equal(first.Id, (await head.SaveImportAsync(request)).Value!.Rows.Single().Registration.Id);
        Assert.Equal(VendorRegistrationStatus.Closed, (await head.ManageRegistrationsAsync(date.Year, date.Month)).Value!.Rows.Single().Status);
        Assert.Single(await db.FishMeatVendorRegistrations.ToListAsync()); Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionLines.ToListAsync());
    }
    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Registry_concurrent_renewals_cannot_duplicate_source_year(bool sameOperation)
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); Guid source;
        await using (var seed = database.CreateContext(w.TenantId)) source = (await Office(seed, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), date.Year - 1,
            FishMeatVendorType.Fish, VendorRegistrationKind.New, "Vendor"))).Value!.Id;
        var one = new RenewVendorRegistrationRequest(Guid.NewGuid(), date.Year);
        var two = sameOperation ? one : one with { ClientOperationId = Guid.NewGuid() };
        async Task Execute(RenewVendorRegistrationRequest request)
        {
            await using var ctx = database.CreateContext(w.TenantId);
            var result = await Office(ctx, w, "SuperAdmin").RenewRegistrationAsync(source, request);
            Assert.True(result.IsSuccess || result.Status == ResultStatus.Conflict);
        }
        await Task.WhenAll(Execute(one), Execute(two));
        await using var verify = database.CreateContext(w.TenantId);
        var winner = Assert.Single(await verify.FishMeatVendorRegistrations.Where(x => x.PriorRegistrationId == source).ToListAsync());
        var replay = await Office(verify, w, "SuperAdmin").RenewRegistrationAsync(source, winner.ClientOperationId == one.ClientOperationId ? one : two);
        Assert.True(replay.IsSuccess); Assert.True(replay.Value!.ExistingOutcome); Assert.Equal(winner.Id, replay.Value.Registration.Id);
    }
}
