using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueSourcePerformance;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class EcfCollectionWorkflowTests
{
    private static MonthlyRentReceiptOptions OfficeReceipts(Guid tenant, FacilityCode facility) =>
        new(true, new HashSet<FacilityCode> { facility }, new HashSet<Guid> { tenant });

    [SkippableFact]
    public async Task Monthly_rent_office_shortcut_is_disabled_by_default_even_for_a_canonical_row()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync(); var seed = await SeedEcfAsync(true); var period = seed.Period;
        var rent = await SeedRentSourcesAsync(seed, [period], true, facilityCode: FacilityCode.NCC);
        await using var context = db.CreateContext(seed.TenantId);
        var flow = new CollectionComposerWorkflow(context, new TestActor(seed.UserId, seed.TenantId), new FixedTenant(seed.TenantId));
        var quote = (await flow.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        Assert.True(quote.CanPostCanonical); Assert.False(quote.CanPostOfficeReceipt);
        var posted = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 600, quote.SourceVersion, PhilippineTime.Today));
        Assert.False(posted.IsSuccess); Assert.Contains("remain disabled", posted.Error);
        Assert.Empty(await context.Collections.ToArrayAsync()); Assert.Empty(await context.PostingOperations.ToArrayAsync());
    }

    [SkippableFact]
    public async Task Legacy_NCC_1200_reference_546564_remains_legacy_and_no_SRC_is_fabricated()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync(); var seed = await SeedEcfAsync(true); var period = seed.Period;
        var rent = await SeedRentSourcesAsync(seed, [period], false, facilityCode: FacilityCode.NCC, monthlyRate: 1200);
        await using var context = db.CreateContext(seed.TenantId);
        var record = await context.PaymentRecords.SingleAsync();
        record.UpdateStatus(PaymentStatus.Paid, 0, updatedBy: "head"); record.SetOrNumber("546564", "head"); await context.SaveChangesAsync();
        var events = await new CollectionActivityReader(context).GetAsync(seed.TenantId, PhilippineTime.Today, PhilippineTime.Today);
        var legacy = Assert.Single(events, x => x.DocumentNumber == "546564");
        Assert.Equal("Legacy", legacy.Authority); Assert.Equal(1200, legacy.Amount); Assert.Null(legacy.ReferenceCode);
        Assert.Empty(await context.Collections.ToArrayAsync()); Assert.Empty(await context.CollectionRemittances.ToArrayAsync());
    }

    [SkippableFact]
    public async Task Monthly_rent_partial_legacy_cutover_requires_dated_receipt_preservation_before_freeze()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync(); var seed = await SeedEcfAsync(true);
        var rent = await SeedRentSourcesAsync(seed, [seed.Period], false, partialAmount: 600, facilityCode: FacilityCode.NCC, monthlyRate: 1200);
        await using var context = db.CreateContext(seed.TenantId);
        var record = await context.PaymentRecords.SingleAsync(); record.MarkSettlementPendingCutover(); await context.SaveChangesAsync();
        var readiness = await new SettlementCutoverWorkflow(context, new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"), new FixedTenant(seed.TenantId))
            .EvaluateReadinessAsync(new(new(CollectionSourceKind.PaymentRecord, record.Id, null)));
        Assert.True(readiness.IsSuccess, readiness.Error); Assert.False(readiness.Value!.Ready);
        Assert.Contains(readiness.Value.BlockingReasons, x => x.Contains("dated receipt/report evidence"));
        Assert.Equal(600, record.PartialAmount); Assert.Empty(await context.Collections.ToArrayAsync());
        Assert.Empty(await context.CollectionSettlementCutovers.Where(x => x.SourceId == record.Id).ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData(FacilityCode.NCC)]
    [InlineData(FacilityCode.TCC)]
    [InlineData(FacilityCode.BBQ)]
    [InlineData(FacilityCode.ICE)]
    public async Task Monthly_rent_Head_and_Mobile_installments_have_distinct_SRC_and_report_adjustments_never_become_cash(FacilityCode facility)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync(); var seed = await SeedEcfAsync(canonical: true);
        var period = seed.Period;
        var rent = await SeedRentSourcesAsync(seed, [period], true, facilityCode: facility, monthlyRate: 1200, sourceOwned: true);
        var collector = await SeedCollectorAsync(seed.TenantId, facility);
        await using var headDb = db.CreateContext(seed.TenantId);
        var tenant = new FixedTenant(seed.TenantId); var actor = new TestActor(seed.UserId, seed.TenantId, "SuperAdmin");
        var report = new GetOfficialMonthlyIncomeQueryHandler(headDb, new LegacyMonthlyIncomeReader(headDb), actor, tenant, new ProofClock());
        var preexistingOtherCash = (await report.Handle(new(PhilippineTime.Today.Year, PhilippineTime.Today.Month), default)).Value!.GrandTotal.SystemAmount;
        var head = new CollectionComposerWorkflow(headDb, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, facility));
        var quote = (await head.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        Assert.Null(quote.PayorId);
        var request = new MobileRentPostRequest(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 600, quote.SourceVersion, PhilippineTime.Today, rent.ContractId);
        var first = await head.PostOfficeRentAsync(request); Assert.True(first.IsSuccess, first.Error);
        var replay = await head.PostOfficeRentAsync(request); Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(first.Value!.CollectionId, replay.Value!.CollectionId); Assert.True(replay.Value.ReturnedExistingOutcome);
        Guid secondId; string secondSrc;
        await using (var mobileDb = db.CreateContext(seed.TenantId))
        {
            var mobile = AsCollector(mobileDb, seed.TenantId, collector);
            var remaining = (await new CollectionComposerWorkflow(mobileDb, actor, tenant).GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
            Assert.Equal(600, remaining.OutstandingAmount);
            var operation = new SyncOfflineOperationDto(Guid.NewGuid(), OfflineOperationKind.RentCollection, PhilippineTime.Today,
                StallId: rent.StallId, ReceivedAmount: 600, BillingYear: period.Year, BillingMonth: period.Month, RentSourceVersion: remaining.SourceVersion);
            var sync = new SyncOfflineCollectionsCommandHandler(null!, null!, new CollectorActor(collector, seed.TenantId), ecfWorkflow: mobile);
            var second = Assert.Single((await sync.Handle(new([operation]), default)).Value!.Results);
            Assert.Equal(SyncResultStatus.Synced, second.Status); Assert.NotEqual(first.Value.ReferenceCode, second.ReferenceCode);
            var syncedAgain = Assert.Single((await sync.Handle(new([operation]), default)).Value!.Results);
            Assert.Equal(second.CollectionId, syncedAgain.CollectionId); Assert.Equal(second.ReferenceCode, syncedAgain.ReferenceCode);
            Assert.Equal(SyncResultStatus.Synced, syncedAgain.Status);
            secondId = second.CollectionId!.Value; secondSrc = second.ReferenceCode!;
        }
        headDb.ChangeTracker.Clear();
        Assert.Equal(2, await headDb.Collections.CountAsync()); Assert.Equal(1200, await headDb.Collections.SumAsync(x => x.TotalAmount));
        Assert.Equal(2, await headDb.Collections.Select(x => x.ReferenceCode).Distinct().CountAsync());
        Assert.Single(await headDb.Collections.Where(x => x.CollectorId == collector).ToListAsync());
        Assert.Single(await headDb.Collections.Where(x => x.CollectorId == null).ToListAsync());
        Assert.Equal(PaymentStatus.Paid, (await headDb.PaymentRecords.SingleAsync()).Status);
        Assert.Empty(await headDb.AccountableDocuments.Where(x => x.CollectionId != null).ToArrayAsync());
        var statement = (await report.Handle(new(PhilippineTime.Today.Year, PhilippineTime.Today.Month), default)).Value!;
        var rowKey = facility == FacilityCode.ICE ? "ICE_PLANT" : "RENT_" + facility;
        var cell = statement.Groups.SelectMany(x => x.Rows).Single(x => x.Key == rowKey).Months[PhilippineTime.Today.Month - 1];
        Assert.Equal((0m, 1200m, 1200m), (cell.Legacy, cell.Canonical, cell.SystemAmount));
        var adjustment = await new ReportGovernanceWorkflow(headDb, actor, tenant, new ProofClock(), new LegacyMonthlyIncomeReader(headDb))
            .AdjustAsync(new(Guid.NewGuid(), rowKey, PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1200, 1254, "Report-only revision"));
        Assert.True(adjustment.IsSuccess, adjustment.Error);
        var performance = (await new GetRevenueSourcePerformanceQueryHandler(report, headDb, tenant).Handle(new(PhilippineTime.Today.Year, PhilippineTime.Today.Month), default)).Value!;
        var source = performance.Rows.Single(x => x.Key == rowKey);
        Assert.Equal((1200m, 1254m, 54m), (source.Collected, source.OfficialAmount, source.AdjustmentAmount));
        Assert.Equal(1200 + preexistingOtherCash, performance.TotalCollected);
        Assert.Equal(2, await headDb.Collections.CountAsync()); Assert.Empty(await headDb.CollectionRemittances.ToArrayAsync());
        var feed = await new CollectionActivityReader(headDb).GetAsync(seed.TenantId, PhilippineTime.Today, PhilippineTime.Today);
        Assert.Equal(1200, feed.Where(x => x.Authority == "Canonical").Sum(x => x.NetAmount));
        Assert.DoesNotContain(feed, x => x.Authority == "Legacy" && x.Lines.Any(l => l.SourceId == rent.PaymentRecordIds[period]));
        await using (var reverseDb = db.CreateContext(seed.TenantId))
        {
            var corrections = new MobileCollectionCorrectionWorkflow(reverseDb, new CollectionSessionStore(reverseDb), null!, null!,
                new CollectionActivityReader(reverseDb), null!, new CollectorActor(collector, seed.TenantId), tenant, new ProofClock());
            var editOperation = Guid.NewGuid();
            var edit = new EditMobileCollectionIntent(editOperation, secondId, new(MobileCorrectionReason.WrongAmount),
                new(editOperation, PhilippineTime.Today, null, [new(Guid.NewGuid(), CollectionSessionItemKind.MonthlyRent, 500,
                    Rent: new(rent.StallId, period.Year, period.Month, 0))]));
            Assert.Equal("RentReplacementRequiresReview", (await corrections.QuoteEditAsync(edit)).Error);
            Assert.Equal("RentReplacementRequiresReview", (await corrections.EditAsync(new(edit, "not-authorized"))).Error);
            Assert.Empty(await reverseDb.CollectionCorrections.ToArrayAsync());
            var remove = new RemoveMobileCollectionRequest(Guid.NewGuid(), secondId, new(MobileCorrectionReason.EnteredByMistake));
            var reversed = await corrections.RemoveAsync(remove); Assert.True(reversed.IsSuccess, reversed.Error);
            Assert.Equal(reversed.Value!.CorrectionId, (await corrections.RemoveAsync(remove)).Value!.CorrectionId);
        }
        headDb.ChangeTracker.Clear();
        var reopened = await headDb.PaymentRecords.SingleAsync(); Assert.Equal(PaymentStatus.Partial, reopened.Status); Assert.Equal(600, reopened.PartialAmount);
        Assert.Equal(600, (await head.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!.OutstandingAmount);
        Assert.Equal(secondSrc, (await headDb.Collections.SingleAsync(x => x.Id == secondId)).ReferenceCode);
        Assert.Equal(1200, await headDb.Collections.SumAsync(x => x.TotalAmount)); Assert.Single(await headDb.CollectionCorrections.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData(900, 875, 25)]
    [InlineData(1000, 980, 20)]
    public async Task Monthly_rent_exact_terminal_remainder_and_concurrent_final_receipts_cannot_oversettle(decimal rental, decimal paid, decimal remainder)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync(); var seed = await SeedEcfAsync(true); var period = seed.Period;
        var rent = await SeedRentSourcesAsync(seed, [period], true, facilityCode: FacilityCode.NCC, monthlyRate: rental);
        var actor = new TestActor(seed.UserId, seed.TenantId); var tenant = new FixedTenant(seed.TenantId);
        await using (var setup = db.CreateContext(seed.TenantId))
        {
            var flow = new CollectionComposerWorkflow(setup, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, FacilityCode.NCC));
            var quote = (await flow.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
            var wrongOwner = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, paid, quote.SourceVersion, PhilippineTime.Today, Guid.NewGuid()));
            Assert.False(wrongOwner.IsSuccess); Assert.Empty(await setup.Collections.ToArrayAsync());
            Assert.True((await flow.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, paid, quote.SourceVersion, PhilippineTime.Today, rent.ContractId))).IsSuccess);
        }
        await using var left = db.CreateContext(seed.TenantId); await using var right = db.CreateContext(seed.TenantId);
        var one = new CollectionComposerWorkflow(left, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, FacilityCode.NCC));
        var two = new CollectionComposerWorkflow(right, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, FacilityCode.NCC));
        var a = (await one.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        var b = (await two.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        Assert.Equal(remainder, a.OutstandingAmount);
        var results = await Task.WhenAll(one.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, remainder, a.SourceVersion, PhilippineTime.Today, rent.ContractId)),
            two.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, remainder, b.SourceVersion, PhilippineTime.Today, rent.ContractId)));
        Assert.Single(results, x => x.IsSuccess);
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Equal(rental, await verify.Collections.SumAsync(x => x.TotalAmount)); Assert.Equal(2, await verify.Collections.CountAsync());
        Assert.Equal(0, (await new CollectionComposerWorkflow(verify, actor, tenant).GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!.OutstandingAmount);
    }

    [SkippableFact]
    public async Task Monthly_rent_legacy_opening_cash_requires_historical_receipt_reconciliation()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync(); var seed = await SeedEcfAsync(true); var period = seed.Period;
        var rent = await SeedRentSourcesAsync(seed, [period], true, partialAmount: 600, facilityCode: FacilityCode.NCC, monthlyRate: 1200);
        await using var context = db.CreateContext(seed.TenantId);
        var flow = new CollectionComposerWorkflow(context, new TestActor(seed.UserId, seed.TenantId), new FixedTenant(seed.TenantId), rentOptions: OfficeReceipts(seed.TenantId, FacilityCode.NCC));
        var quote = (await flow.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        Assert.Equal(600, quote.OutstandingAmount); Assert.False(quote.CanPostCanonical); Assert.True(quote.RequiresLegacyReconciliation);
        Assert.False((await flow.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 600, quote.SourceVersion, PhilippineTime.Today, rent.ContractId))).IsSuccess);
        Assert.Empty(await context.Collections.ToArrayAsync());
    }
}
