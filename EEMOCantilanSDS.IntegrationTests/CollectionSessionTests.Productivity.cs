using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueSourcePerformance;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;
public sealed partial class CollectionSessionTests
{
    private static MobileCollectionCorrectionWorkflow Corrections(AppDbContext db, World w)
    {
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(PhilippineTime.Today), null!, new NoMarketDays());
        return new(db, new CollectionSessionStore(db), sources, NativeWorkflow(db, w, sources), new CollectionActivityReader(db), Office(db, w),
            new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(PhilippineTime.Today));
    }
    [SkippableFact]
    public async Task Terminal_optional_name_is_frozen_and_own_current_collection_can_be_replaced_and_removed_without_deleting_money_evidence()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var vehicle = VehicleClass.Create(w.TenantId, "JEEPNEY", "Jeepney", "Head");
        db.AddRange(vehicle, VehicleClassRate.Create(w.TenantId, vehicle.Id, date, 20m, "Head")); await db.SaveChangesAsync();
        var masters = await db.Payors.CountAsync();
        var charge = new SourceNativeChargeIntent(CollectorOperationCodes.Terminal, Section: TerminalSection.PullPulVansCargoVans,
            VehicleClassId: vehicle.Id, CashTicketCount: 1);
        var posted = (await Office(db, w).PostAsync(new(Guid.NewGuid(), date, 20m, charge, "  Juan Dela Cruz  "))).Value!;
        var frozen = Assert.Single((await Office(db, w).ActivityAsync(date, date)).Value!);
        Assert.Equal("Juan Dela Cruz", frozen.PayerSnapshot); Assert.Equal("Jeepney", frozen.VehicleClassName);
        Assert.Equal(masters, await db.Payors.CountAsync()); Assert.Empty(await db.FishMeatVendorRegistrations.ToListAsync());
        var flow = Corrections(db, w);
        var recent = Assert.Single((await flow.RecentAsync()).Value!.Rows); Assert.True(recent.CanEdit); Assert.True(recent.CanRemove);
        Assert.Equal(charge, recent.EditSource!.Native); Assert.Equal("Juan Dela Cruz", recent.EditSource.PayerSnapshot);
        var operation = Guid.NewGuid();
        var edit = new EditMobileCollectionIntent(operation, posted.CollectionId, new(MobileCorrectionReason.WrongAmount),
            new(operation, date, null, [new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 40m, Native: charge with { CashTicketCount = 2 })], PayerSnapshot: "Juan corrected"));
        var quote = (await flow.QuoteEditAsync(edit)).Value!; Assert.True(quote.CanRecord);
        Assert.Empty(await db.CollectionCorrections.ToListAsync()); Assert.Single(await db.Collections.ToListAsync());
        var edited = await flow.EditAsync(new(edit, quote.QuoteFingerprint!)); Assert.True(edited.IsSuccess, edited.Error);
        var result = edited.Value!; Assert.NotEqual(posted.ReferenceCode, result.Replacement!.ReferenceCode);
        Assert.Equal(posted.CollectionId, (await db.CollectionCorrections.SingleAsync()).OriginalCollectionId);
        Assert.Equal(result.Replacement.CollectionId, (await db.CollectionCorrections.SingleAsync()).ReplacementCollectionId);
        Assert.Equal(20m, (await db.Collections.SingleAsync(x => x.Id == posted.CollectionId)).TotalAmount);
        Assert.Equal(result.Replacement.CollectionId, (await flow.EditAsync(new(edit, "lost response"))).Value!.Replacement!.CollectionId);
        Assert.Equal("CorrectionIntentConflict", (await flow.EditAsync(new(edit with { Reason = new(MobileCorrectionReason.Duplicate) }, "lost"))).Error);
        var activity = await new CollectionActivityReader(db).GetAsync(w.TenantId, date, date); Assert.Equal(40m, activity.Sum(x => x.NetAmount));
        Assert.False((await flow.RecentAsync()).Value!.Rows.Single(x => x.Collection.CollectionId == posted.CollectionId).CanRemove);
        var remove = new RemoveMobileCollectionRequest(Guid.NewGuid(), result.Replacement.CollectionId, new(MobileCorrectionReason.EnteredByMistake));
        var removed = await flow.RemoveAsync(remove); Assert.True(removed.IsSuccess, removed.Error);
        Assert.Equal(removed.Value!.CorrectionId, (await flow.RemoveAsync(remove)).Value!.CorrectionId);
        Assert.Equal(2, await db.Collections.CountAsync()); Assert.Equal(2, await db.CollectionLines.CountAsync()); Assert.Equal(2, await db.CollectionCorrections.CountAsync());
        Assert.Equal(0m, (await new CollectionActivityReader(db).GetAsync(w.TenantId, date, date)).Sum(x => x.NetAmount));
        var report = (await new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db),
            new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin"), new Tenant(w.TenantId), new Clock(date)).Handle(new(date.Year, date.Month), default)).Value!;
        Assert.Equal(0m, report.GrandTotal.Canonical);
        Assert.Equal(0m, (await new RemittanceWorkflow(db, new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin"), new Tenant(w.TenantId)).GetReviewAsync(new(date, date))).Value!.Total);
    }
    [SkippableFact]
    public async Task Correction_blocks_other_collector_remitted_and_previous_business_date_and_void_remittance_restores_eligibility()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var request = new SourceNativeCollectionRequest(Guid.NewGuid(), date, 100m, new(CollectorOperationCodes.Terminal, Section: TerminalSection.ComfortRoom));
        var posted = (await Office(db, w).PostAsync(request)).Value!;
        var secondCollector = CollectorUser.Create("Other collector", "OTHER", "other-" + Guid.NewGuid().ToString("N")[..8], null, null, new HashedPassword("h"), w.TenantId);
        db.Add(secondCollector); await db.SaveChangesAsync();
        var other = Corrections(db, w with { CollectorId = secondCollector.Id });
        Assert.Equal("DifferentCollector", (await other.RemoveAsync(new(Guid.NewGuid(), posted.CollectionId, new(MobileCorrectionReason.Duplicate)))).Error);
        var remit = new RemittanceWorkflow(db, new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin"), new Tenant(w.TenantId));
        var remitted = await remit.RecordAsync(new(Guid.NewGuid(), w.CollectorId, date, date, date, RevenueInstrumentType.CashTicket,
            [posted.CollectionId], 100m, null, null)); Assert.True(remitted.IsSuccess, remitted.Error);
        var flow = Corrections(db, w);
        Assert.Equal("Remitted", Assert.Single((await flow.RecentAsync()).Value!.Rows).BlockReasonCode);
        Assert.Equal("Remitted", (await flow.RemoveAsync(new(Guid.NewGuid(), posted.CollectionId, new(MobileCorrectionReason.WrongAmount)))).Error);
        var operation = Guid.NewGuid();
        var lockedEdit = new EditMobileCollectionIntent(operation, posted.CollectionId, new(MobileCorrectionReason.WrongAmount),
            new(operation, date, null, [new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 10m, Native: request.Charge)]));
        Assert.Equal("Remitted", (await flow.QuoteEditAsync(lockedEdit)).Error);
        Assert.Equal("Remitted", (await flow.EditAsync(new(lockedEdit, "old quote"))).Error);
        Assert.True((await remit.VoidAsync(remitted.Value!.Row.Id, new("Test void"))).IsSuccess);
        Assert.True(Assert.Single((await flow.RecentAsync()).Value!.Rows).CanRemove);
        if (date > OfficeCollectionWorkflow.EffectiveFrom)
        {
            var old = (await Office(db, w).PostAsync(request with { ClientOperationId = Guid.NewGuid(), BusinessDate = date.AddDays(-1) })).Value!;
            Assert.Equal("OutsideCurrentBusinessDate", (await flow.RemoveAsync(new(Guid.NewGuid(), old.CollectionId, new(MobileCorrectionReason.Duplicate)))).Error);
        }
    }
    [SkippableFact]
    public async Task Registered_weighing_edit_requotes_quantity_and_cannot_override_effective_rate()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var vendor = (await Office(db, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), date.Year, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Pantom Dant"))).Value!;
        var charge = new SourceNativeChargeIntent(CollectorOperationCodes.WeightAndMeasure, vendor.Id, Kilograms: 3m);
        var request = new SourceNativeCollectionRequest(Guid.NewGuid(), date, 66m, charge);
        var posted = (await Office(db, w).PostAsync(request)).Value!;
        var operation = Guid.NewGuid(); var flow = Corrections(db, w);
        var edit = new EditMobileCollectionIntent(operation, posted.CollectionId, new(MobileCorrectionReason.WrongAmount),
            new(operation, date, null, [new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 1m, Native: charge with { Kilograms = 2m })],
                SourceIdentity: new(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id)));
        Assert.Equal("AmountChanged", Assert.Single((await flow.QuoteEditAsync(edit)).Value!.Problems).Code);
        Assert.Empty(await db.CollectionCorrections.ToListAsync());
        edit = edit with { Replacement = edit.Replacement with { Items = [edit.Replacement.Items[0] with { ConfirmedAmount = 44m }] } };
        var quote = (await flow.QuoteEditAsync(edit)).Value!; Assert.True(quote.CanRecord);
        var changed = await flow.EditAsync(new(edit, quote.QuoteFingerprint!)); Assert.True(changed.IsSuccess, changed.Error);
        Assert.Equal(44m, changed.Value!.Replacement!.Amount);
        Assert.Empty(await db.DailyCollections.ToListAsync());
        Assert.Equal(44m, (await new CollectionActivityReader(db).GetAsync(w.TenantId, date, date)).Sum(x => x.NetAmount));
    }
    [SkippableFact]
    public async Task Registry_import_previews_without_mutation_requires_duplicate_review_and_save_revalidates_without_collections()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin");
        var request = new VendorRegistryImportRequest([
            new(1, new(Guid.NewGuid(), date.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "  Ana Fish  ")),
            new(2, new(Guid.NewGuid(), date.Year, FishMeatVendorType.Meat, VendorRegistrationKind.Renew, "Ben Meat"))]);
        var preview = (await head.PreviewImportAsync(request)).Value!; Assert.True(preview.CanSave); Assert.Equal("Ana Fish", preview.Rows[0].Registration.DisplayName);
        Assert.Empty(await db.FishMeatVendorRegistrations.ToListAsync());
        var saved = (await head.SaveImportAsync(request)).Value!; Assert.Equal(2, saved.Rows.Count);
        Assert.Equal(saved.Rows.Select(x => x.Registration.Id), (await head.SaveImportAsync(request)).Value!.Rows.Select(x => x.Registration.Id));
        var duplicate = new VendorRegistryImportRequest([new(1, request.Rows[0].Registration with { ClientOperationId = Guid.NewGuid() })]);
        Assert.Equal(VendorRegistryImportState.PossibleDuplicateRequiresReview, Assert.Single((await head.PreviewImportAsync(duplicate)).Value!.Rows).State);
        Assert.Equal("ImportNeedsReview", (await head.SaveImportAsync(duplicate)).Error);
        duplicate = duplicate with { Rows = [duplicate.Rows[0] with { ConfirmSeparateRegistration = true }] };
        Assert.True((await head.SaveImportAsync(duplicate)).IsSuccess);
        Assert.Equal(3, await db.FishMeatVendorRegistrations.CountAsync());
        var invalid = duplicate with { Rows = [duplicate.Rows[0] with { Registration = duplicate.Rows[0].Registration with { VendorType = (FishMeatVendorType)3 } }] };
        Assert.Equal(VendorRegistryImportState.Invalid, Assert.Single((await head.PreviewImportAsync(invalid)).Value!.Rows).State);
        Assert.Equal("ImportNeedsReview", (await head.SaveImportAsync(invalid)).Error);
        Assert.Empty(await db.Collections.ToListAsync());
        Assert.Empty(await db.CollectionLines.ToListAsync());
        Assert.Equal(ResultStatus.Forbidden, (await Office(db, w).PreviewImportAsync(request)).Status);
        var summary = (await head.RegistrySummaryAsync(date.Year)).Value!;
        Assert.Equal((3, 2, 1), (summary.TotalRegistered, summary.FishCount, summary.MeatCount));
    }
    [SkippableFact]
    public async Task Revenue_performance_uses_the_official_terminal_rows_without_counting_subtotals_twice()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        foreach (var section in Enum.GetValues<TerminalSection>())
            Assert.True((await Office(db, w).PostAsync(new(Guid.NewGuid(), date, 100m, new(CollectorOperationCodes.Terminal, Section: section)))).IsSuccess);
        var official = new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin"), new Tenant(w.TenantId), new Clock(date));
        var performance = (await new GetRevenueSourcePerformanceQueryHandler(official, db, new Tenant(w.TenantId)).Handle(new(date.Year, date.Month), default)).Value!;
        Assert.Equal(300m, performance.TotalCollected); Assert.Equal(300m, performance.Rows.Sum(x => x.Collected));
        var terminal = performance.Rows.Where(x => x.Key.StartsWith("TERMINAL_")).ToArray(); Assert.Equal(3, terminal.Length);
        Assert.All(terminal, x => { Assert.Equal(100m, x.Collected); Assert.Equal(1, x.TransactionCount); Assert.False(x.AwaitingPlacement); });
        Assert.Equal("RENT", performance.Rows.Single(x => x.Key == "RENT_BBQ").GroupKey);
        Assert.Equal("SLAUGHTERHOUSE", performance.Rows.Single(x => x.Key == "SLAUGHTERHOUSE").GroupKey);
        var report = (await official.Handle(new(date.Year, date.Month), default)).Value!;
        Assert.Equal(300m, report.Sections!.Single(x => x.Key == "B").Total.Canonical);
        Assert.Contains(report.Sections!, x => x.Key == "A"); Assert.Contains(report.Sections!, x => x.Key == "C");
        Assert.Equal(300m, report.GrandTotal.Canonical);
    }
    [SkippableFact]
    public async Task Walkup_vendor_fee_is_self_correctable_and_failed_replacement_rolls_back_the_reversal()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var posted = (await Office(db, w).PostAsync(new(Guid.NewGuid(), date, 100m, new(CollectorOperationCodes.FishMeatVendorFee), "Walk-up vendor"))).Value!;
        var flow = Corrections(db, w); Assert.True(Assert.Single((await flow.RecentAsync()).Value!.Rows).CanEdit);
        var operation = Guid.NewGuid();
        var edit = new EditMobileCollectionIntent(operation, posted.CollectionId, new(MobileCorrectionReason.WrongPayerOrSource),
            new(operation, date, null, [new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 100m,
                Native: new(CollectorOperationCodes.Terminal, Section: TerminalSection.ComfortRoom))]));
        var quote = (await flow.QuoteEditAsync(edit)).Value!;
        Assert.Equal("ReplacementSourceMismatch", (await flow.EditAsync(new(edit, quote.QuoteFingerprint!))).Error);
        Assert.Single(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionCorrections.ToListAsync());
        Assert.Equal(100m, (await new CollectionActivityReader(db).GetAsync(w.TenantId, date, date)).Sum(x => x.NetAmount));
        Assert.True((await flow.RemoveAsync(new(Guid.NewGuid(), posted.CollectionId, new(MobileCorrectionReason.Duplicate)))).IsSuccess);
        Assert.Empty(await db.FishMeatVendorRegistrations.ToListAsync());
    }
    [SkippableFact]
    public async Task Removing_prepared_Water_payment_restores_outstanding_without_changing_the_assessment()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var bill = UtilityBill.Create(w.StallId, date.Year, date.Month, 0, 1, 500m, 0, 1, 100m, "head");
        db.AddRange(bill, CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
            CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, date, Guid.NewGuid(), "head", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var source = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(date), null!, new NoMarketDays());
        var flow = NativeWorkflow(db, w, source);
        var water = new WcfCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(date));
        var current = (await water.GetMobileSourcesAsync(date.Year, date.Month)).Value!.Single();
        var occupancy = await db.Contracts.SingleAsync(x => x.StallId == w.StallId);
        var intent = new CollectionSessionIntent(Guid.NewGuid(), date, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Water, 20m, Water: new(w.StallId, date.Year, date.Month, bill.Id, current.WaterSourceVersion))],
            SourceIdentity: new(SourceIdentityKind.Occupancy, occupancy.Id));
        var posted = await Record(flow, intent);
        var editSource = Assert.Single((await Corrections(db, w).RecentAsync()).Value!.Rows).EditSource!;
        Assert.Equal(CollectionSessionItemKind.Water, editSource.Kind); Assert.Equal(occupancy.Id, editSource.SourceIdentity!.Id);
        Assert.Equal(bill.Id, editSource.Identity.UtilityBillId);
        Assert.Equal(80m, (await water.GetMobileSourcesAsync(date.Year, date.Month)).Value!.Single().OutstandingAmount);
        var correction = await Corrections(db, w).RemoveAsync(new(Guid.NewGuid(), posted.Collections.Single().CollectionId, new(MobileCorrectionReason.EnteredByMistake)));
        Assert.True(correction.IsSuccess, correction.Error);
        Assert.Equal(100m, (await water.GetMobileSourcesAsync(date.Year, date.Month)).Value!.Single().OutstandingAmount);
        Assert.Equal(100m, (await db.UtilityBills.SingleAsync()).WaterCharge); Assert.Single(await db.Collections.ToListAsync());
    }
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_self_removal_has_one_reversal_and_unknown_response_retries_the_saved_outcome(bool sameOperation)
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(); Guid id;
        await using (var setup = database.CreateContext(w.TenantId))
        {
            await EnableOffice(setup, w);
            id = (await Office(setup, w).PostAsync(new(Guid.NewGuid(), date, 100m, new(CollectorOperationCodes.Terminal, Section: TerminalSection.Tricycad)))).Value!.CollectionId;
        }
        var first = new RemoveMobileCollectionRequest(Guid.NewGuid(), id, new(MobileCorrectionReason.Duplicate));
        var second = first with { ClientOperationId = sameOperation ? first.ClientOperationId : Guid.NewGuid() };
        await using var a = database.CreateContext(w.TenantId); await using var b = database.CreateContext(w.TenantId);
        var results = await Task.WhenAll(Corrections(a, w).RemoveAsync(first), Corrections(b, w).RemoveAsync(second));
        Assert.Contains(results, x => x.IsSuccess);
        await using var verify = database.CreateContext(w.TenantId);
        Assert.Single(await verify.CollectionCorrections.ToListAsync()); Assert.Single(await verify.Collections.ToListAsync());
        Assert.Equal(0m, (await new CollectionActivityReader(verify).GetAsync(w.TenantId, date, date)).Sum(x => x.NetAmount));
        var winnerRequest = results[0].IsSuccess ? first : second;
        Assert.True((await Corrections(verify, w).RemoveAsync(winnerRequest)).Value!.ExistingOutcome);
        if (sameOperation) Assert.Equal(results.First(x => x.IsSuccess).Value!.CorrectionId,
            (await Corrections(verify, w).RemoveAsync(second)).Value!.CorrectionId);
    }
}
