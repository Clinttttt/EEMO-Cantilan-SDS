using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueSourcePerformance;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// One NCC canonical month collected through BOTH channels (Head Office and the collector's offline sync) and then remitted: a single
/// balance, one SRC per real payment event, stale quotes refused, corrections reopening the right balance, and remittance covering only
/// collector cash. Legacy rows are never converted by any channel. NCC is used because it is the only enabled facility.
/// </summary>
public sealed partial class EcfCollectionWorkflowTests
{
    private sealed record Channels(Seed Seed, RentSeed Rent, Guid Collector, TestActor Head, FixedTenant Tenant, FacilityCode Facility = FacilityCode.NCC);

    private async Task<Channels> ArrangeChannelsAsync(bool canonical = true, decimal partial = 0m, decimal rate = 1200m, FacilityCode facility = FacilityCode.NCC)
    {
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true, facility == FacilityCode.NPM ? BillingArchetype.MonthlyRental : BillingArchetype.DailyStall);
        var rent = await SeedRentSourcesAsync(seed, [seed.Period], canonical, partialAmount: canonical ? 0m : partial, facilityCode: facility,
            monthlyRate: rate, sourceOwned: true);
        var collector = await SeedCollectorAsync(seed.TenantId, facility);
        return new(seed, rent, collector, new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"), new FixedTenant(seed.TenantId), facility);
    }

    private CollectionComposerWorkflow Office(AppDbContext ctx, Channels c) =>
        new(ctx, c.Head, c.Tenant, rentOptions: OfficeReceipts(c.Seed.TenantId, c.Facility));

    private async Task<long> VersionAsync(Channels c)
    {
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        return (await Office(ctx, c).GetRentObligationAsync(c.Rent.StallId, c.Seed.Period.Year, c.Seed.Period.Month)).Value!.SourceVersion;
    }

    private async Task<Result<EcfPostOutcomeDto>> HeadPostAsync(Channels c, decimal amount, long version, Guid? operation = null)
    {
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        return await Office(ctx, c).PostOfficeRentAsync(new(operation ?? Guid.NewGuid(), c.Rent.StallId, c.Seed.Period.Year, c.Seed.Period.Month,
            amount, version, PhilippineTime.Today, c.Rent.ContractId));
    }

    private async Task<SyncOperationResultDto> MobilePostAsync(Channels c, decimal amount, long version, Guid? operation = null)
    {
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var mobile = AsCollector(ctx, c.Seed.TenantId, c.Collector);
        var op = new SyncOfflineOperationDto(operation ?? Guid.NewGuid(), OfflineOperationKind.RentCollection, PhilippineTime.Today,
            StallId: c.Rent.StallId, ReceivedAmount: amount, BillingYear: c.Seed.Period.Year, BillingMonth: c.Seed.Period.Month, RentSourceVersion: version);
        var sync = new SyncOfflineCollectionsCommandHandler(null!, null!, new CollectorActor(c.Collector, c.Seed.TenantId), ecfWorkflow: mobile);
        return Assert.Single((await sync.Handle(new([op]), default)).Value!.Results);
    }

    private async Task<(decimal Outstanding, PaymentStatus Status, int Collections, decimal Gross)> StateAsync(Channels c)
    {
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var quote = (await Office(ctx, c).GetRentObligationAsync(c.Rent.StallId, c.Seed.Period.Year, c.Seed.Period.Month)).Value!;
        var record = await ctx.PaymentRecords.AsNoTracking().SingleAsync(x => x.Id == c.Rent.PaymentRecordIds[c.Seed.Period]);
        return (quote.OutstandingAmount, record.Status, await ctx.Collections.CountAsync(), await ctx.Collections.SumAsync(x => x.TotalAmount));
    }

    [SkippableTheory]
    [InlineData(FacilityCode.NCC)] [InlineData(FacilityCode.NPM)]
    public async Task Head_and_Mobile_installments_share_one_balance_with_stale_quotes_refused_and_replays_exact(FacilityCode facility)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync(facility: facility);
        var v0 = await VersionAsync(c);

        var headFirst = await HeadPostAsync(c, 500, v0); Assert.True(headFirst.IsSuccess, headFirst.Error);
        var stale = await MobilePostAsync(c, 400, v0);                       // quoted before the Head receipt
        Assert.NotEqual(SyncResultStatus.Synced, stale.Status);
        Assert.Equal(1, (await StateAsync(c)).Collections);

        var operation = Guid.NewGuid(); var v1 = await VersionAsync(c);
        var mobile = await MobilePostAsync(c, 400, v1, operation);
        Assert.Equal(SyncResultStatus.Synced, mobile.Status);
        var again = await MobilePostAsync(c, 400, v1, operation);             // app restart / lost reply: the device resends the identical payload
        var changed = await MobilePostAsync(c, 400, v0, operation);           // the same operation with a different payload is never accepted
        Assert.NotEqual(SyncResultStatus.Synced, changed.Status);
        Assert.Equal((SyncResultStatus.Synced, mobile.CollectionId, mobile.ReferenceCode), (again.Status, again.CollectionId, again.ReferenceCode));

        var staleHead = await HeadPostAsync(c, 300, v0); Assert.False(staleHead.IsSuccess);
        var headFinal = await HeadPostAsync(c, 300, await VersionAsync(c)); Assert.True(headFinal.IsSuccess, headFinal.Error);
        Assert.False((await HeadPostAsync(c, 1, await VersionAsync(c))).IsSuccess);   // nothing left to pay

        var state = await StateAsync(c);
        Assert.Equal((0m, PaymentStatus.Paid, 3, 1200m), state);
        await using var read = db.CreateContext(c.Seed.TenantId);
        var all = await read.Collections.AsNoTracking().OrderBy(x => x.ReferenceNumber).ToListAsync();
        Assert.Equal(3, all.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.Equal([null, c.Collector, null], all.Select(x => x.CollectorId).ToArray());                  // truthful attribution per channel
        Assert.Empty(await read.AccountableDocuments.Where(x => x.CollectionId != null).ToArrayAsync());    // no physical OR invented
    }

    [SkippableTheory]
    [InlineData(FacilityCode.NCC)] [InlineData(FacilityCode.NPM)]
    public async Task Concurrent_Head_and_Mobile_final_receipts_settle_exactly_once(FacilityCode facility)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync(facility: facility);
        Assert.True((await HeadPostAsync(c, 800, await VersionAsync(c))).IsSuccess);
        var version = await VersionAsync(c);

        var results = await Task.WhenAll(
            HeadPostAsync(c, 400, version).ContinueWith(t => t.Result.IsSuccess),
            MobilePostAsync(c, 400, version).ContinueWith(t => t.Result.Status == SyncResultStatus.Synced));
        Assert.Equal(1, results.Count(x => x));

        Assert.Equal((0m, PaymentStatus.Paid, 2, 1200m), await StateAsync(c));
    }

    [SkippableTheory]
    [InlineData(FacilityCode.NCC)] [InlineData(FacilityCode.NPM)]
    public async Task Reversal_recovers_the_balance_the_old_quote_stays_refused_and_collector_cash_is_not_the_Heads_to_reverse(FacilityCode facility)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync(facility: facility);
        var officeReceipt = await HeadPostAsync(c, 600, await VersionAsync(c)); Assert.True(officeReceipt.IsSuccess, officeReceipt.Error);
        var collectorReceipt = await MobilePostAsync(c, 600, await VersionAsync(c)); Assert.Equal(SyncResultStatus.Synced, collectorReceipt.Status);
        var preReversal = await VersionAsync(c);

        await using (var ctx = db.CreateContext(c.Seed.TenantId))
        {
            var head = new HeadCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), c.Head, c.Tenant, new MutableTestClock(PhilippineTime.Today));
            Assert.Equal("CollectorRecorded", (await head.ReverseAsync(new(Guid.NewGuid(), collectorReceipt.CollectionId!.Value, "Not the Head's cash"))).Error);
            var done = await head.ReverseAsync(new(Guid.NewGuid(), officeReceipt.Value!.CollectionId, "Wrong amount keyed"));
            Assert.True(done.IsSuccess, done.Error);
        }
        Assert.Equal((600m, PaymentStatus.Partial, 2, 1200m), await StateAsync(c));                         // originals kept, balance reopened once

        Assert.NotEqual(SyncResultStatus.Synced, (await MobilePostAsync(c, 600, preReversal)).Status);      // the pre-reversal quote is stale
        var recovered = await MobilePostAsync(c, 600, await VersionAsync(c));
        Assert.Equal(SyncResultStatus.Synced, recovered.Status);
        Assert.Equal(3, new[] { officeReceipt.Value!.ReferenceCode, collectorReceipt.ReferenceCode, recovered.ReferenceCode }.Distinct().Count());
        Assert.Equal((0m, PaymentStatus.Paid, 3, 1800m), await StateAsync(c));                              // gross 1,800 of receipts; 1,200 is the net rent

        await using var read = db.CreateContext(c.Seed.TenantId);
        var correction = await read.CollectionCorrections.SingleAsync();
        Assert.Equal((officeReceipt.Value.CollectionId, -600m), (correction.OriginalCollectionId, correction.FinancialEffectAmount));
        Assert.Equal(600m, (await read.Collections.AsNoTracking().SingleAsync(x => x.Id == officeReceipt.Value.CollectionId)).TotalAmount);   // the original is immutable
        Assert.Empty(await read.CollectionRemittances.ToArrayAsync());                                      // a reversal is not a deposit or refund
    }

    [SkippableTheory]
    [InlineData(FacilityCode.NCC)] [InlineData(FacilityCode.NPM)]
    public async Task No_channel_converts_a_legacy_partial_or_paid_month_into_a_canonical_receipt(FacilityCode facility)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync(canonical: false, partial: 600, facility: facility);
        var version = await VersionAsync(c);

        Assert.False((await HeadPostAsync(c, 600, version)).IsSuccess);
        Assert.NotEqual(SyncResultStatus.Synced, (await MobilePostAsync(c, 600, version)).Status);

        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var record = await ctx.PaymentRecords.SingleAsync();
        Assert.Equal((SettlementAuthority.Legacy, 600m, PaymentStatus.Partial), (record.SettlementAuthorityState, record.PartialAmount, record.Status));
        Assert.Empty(await ctx.Collections.ToArrayAsync());
        // Refused attempts may leave idempotency evidence, but never a success or a Collection.
        Assert.All(await ctx.PostingOperations.AsNoTracking().ToArrayAsync(), x => { Assert.Null(x.CollectionId); Assert.NotEqual(PostingOperationStatus.Succeeded, x.Status); });
    }

    private RemittanceWorkflow Remit(AppDbContext ctx, Channels c) => new(ctx, c.Head, c.Tenant);

    [SkippableTheory]
    [InlineData(FacilityCode.NCC)] [InlineData(FacilityCode.NPM)]
    public async Task Remittance_covers_only_collector_cash_once_and_locked_collections_cannot_be_reversed(FacilityCode facility)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync(facility: facility);
        var today = PhilippineTime.Today;
        var officeReceipt = await HeadPostAsync(c, 600, await VersionAsync(c)); Assert.True(officeReceipt.IsSuccess, officeReceipt.Error);
        var collectorReceipt = await MobilePostAsync(c, 600, await VersionAsync(c)); Assert.Equal(SyncResultStatus.Synced, collectorReceipt.Status);

        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var review = (await Remit(ctx, c).GetReviewAsync(new(today, today))).Value!;
        // Office-origin receipts have no collector, so no collector remits them: a recorded limitation, not an invented procedure.
        var scope = Assert.Single(review.Collectors);
        Assert.Equal((c.Collector, 600m), (scope.CollectorId, scope.ExpectedAmount));
        Assert.Equal([collectorReceipt.ReferenceCode], scope.Collections.Select(x => x.ReferenceCode).ToArray());
        Assert.DoesNotContain(review.Collectors.SelectMany(x => x.Collections), x => x.ReferenceCode == officeReceipt.Value!.ReferenceCode);

        var operation = Guid.NewGuid();
        RecordRemittanceRequest Request(Guid op) => new(op, c.Collector, today, today, today, null, [collectorReceipt.CollectionId!.Value], 600m, "Synthetic turnover", null);
        var recorded = await Remit(ctx, c).RecordAsync(Request(operation)); Assert.True(recorded.IsSuccess, recorded.Error);
        var replay = await Remit(ctx, c).RecordAsync(Request(operation));
        Assert.True(replay.IsSuccess); Assert.Equal(recorded.Value!.Row.Id, replay.Value!.Row.Id);                  // same operation: same record
        var second = await Remit(ctx, c).RecordAsync(Request(Guid.NewGuid()));
        Assert.False(second.IsSuccess);                                                                     // already covered
        Assert.Single(await ctx.CollectionRemittances.AsNoTracking().ToArrayAsync());
        Assert.Single(await ctx.CollectionRemittanceCoverages.AsNoTracking().Where(x => x.IsActive).ToArrayAsync());

        // The accountability position is collector cash only: the 600 Office receipt is in no collector's collected, remitted or unremitted figure.
        var position = (await Remit(ctx, c).GetPositionAsync(today, today)).Value!;
        Assert.Equal((600m, 600m, 0m), (position.Collected, position.Remitted, position.Unremitted));
        // ...yet it is not hidden: it is reported apart as Office-origin cash, never marked remitted and never added to a collector.
        Assert.Equal((600m, 1), (position.OfficeOriginCollected, position.OfficeOriginCount));
        var headReview = new HeadCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), c.Head, c.Tenant, new MutableTestClock(today));
        Assert.True((await headReview.ReviewAsync(officeReceipt.Value!.CollectionId)).Value!.OfficeOrigin);
        Assert.False((await headReview.ReviewAsync(collectorReceipt.CollectionId!.Value)).Value!.OfficeOrigin);

        // Locked cash: neither the Head nor the collector can reverse a remitted collection; the Office receipt is unaffected by the remittance.
        var head = new HeadCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), c.Head, c.Tenant, new MutableTestClock(today));
        Assert.Equal("Remitted", (await head.ReverseAsync(new(Guid.NewGuid(), collectorReceipt.CollectionId!.Value, "x"))).Error);
        var corrections = new MobileCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), null!, null!, new CollectionActivityReader(ctx), null!,
            new CollectorActor(c.Collector, c.Seed.TenantId), c.Tenant, new ProofClock());
        var selfCorrection = await corrections.RemoveAsync(new(Guid.NewGuid(), collectorReceipt.CollectionId!.Value, new(MobileCorrectionReason.EnteredByMistake)));
        Assert.False(selfCorrection.IsSuccess); Assert.Equal("Remitted", selfCorrection.Error);
        Assert.True((await head.ReviewAsync(officeReceipt.Value!.CollectionId)).Value!.Eligibility.CanCorrect);

        // Reversing the Office receipt creates no remittance, deposit or coverage change.
        Assert.True((await head.ReverseAsync(new(Guid.NewGuid(), officeReceipt.Value.CollectionId, "Wrong amount keyed"))).IsSuccess);
        ctx.ChangeTracker.Clear();
        Assert.Single(await ctx.CollectionRemittances.AsNoTracking().ToArrayAsync());
        Assert.Single(await ctx.CollectionRemittanceCoverages.AsNoTracking().Where(x => x.IsActive).ToArrayAsync());
        Assert.Equal(600m, (await ctx.CollectionRemittances.AsNoTracking().SingleAsync()).RemittedAmount);
        var afterReversal = (await Remit(ctx, c).GetPositionAsync(today, today)).Value!;
        Assert.Equal((600m, 600m, 0m), (afterReversal.Collected, afterReversal.Remitted, afterReversal.Unremitted));   // collector figures unchanged
        Assert.Equal((0m, 1), (afterReversal.OfficeOriginCollected, afterReversal.OfficeOriginCount));                  // Office cash net of its reversal
        Assert.Equal((600m, PaymentStatus.Partial, 2, 1200m), await StateAsync(c));
    }

    [SkippableTheory]
    [InlineData(FacilityCode.NCC)] [InlineData(FacilityCode.NPM)]
    public async Task Concurrent_remittances_of_one_collection_and_collector_self_reversal_keep_cash_accounting_consistent(FacilityCode facility)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync(facility: facility);
        var today = PhilippineTime.Today;
        var kept = await MobilePostAsync(c, 400, await VersionAsync(c)); Assert.Equal(SyncResultStatus.Synced, kept.Status);
        var mistaken = await MobilePostAsync(c, 200, await VersionAsync(c)); Assert.Equal(SyncResultStatus.Synced, mistaken.Status);

        await using (var ctx = db.CreateContext(c.Seed.TenantId))
        {
            var corrections = new MobileCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), null!, null!, new CollectionActivityReader(ctx), null!,
                new CollectorActor(c.Collector, c.Seed.TenantId), c.Tenant, new ProofClock());
            Assert.True((await corrections.RemoveAsync(new(Guid.NewGuid(), mistaken.CollectionId!.Value, new(MobileCorrectionReason.EnteredByMistake)))).IsSuccess);
        }
        await using var left = db.CreateContext(c.Seed.TenantId); await using var right = db.CreateContext(c.Seed.TenantId);
        var review = (await Remit(left, c).GetReviewAsync(new(today, today))).Value!;
        Assert.Equal(400m, Assert.Single(review.Collectors).ExpectedAmount);                                // corrected net, not the 600 gross
        Assert.Equal([kept.ReferenceCode], review.Collectors.Single().Collections.Select(x => x.ReferenceCode).ToArray());

        RecordRemittanceRequest Request() => new(Guid.NewGuid(), c.Collector, today, today, today, null, [kept.CollectionId!.Value], 400m, "Synthetic turnover", null);
        var results = await Task.WhenAll(Remit(left, c).RecordAsync(Request()), Remit(right, c).RecordAsync(Request()));
        Assert.Single(results, x => x.IsSuccess);
        await using var verify = db.CreateContext(c.Seed.TenantId);
        Assert.Single(await verify.CollectionRemittances.AsNoTracking().ToArrayAsync());
        Assert.Single(await verify.CollectionRemittanceCoverages.AsNoTracking().Where(x => x.CollectionId == kept.CollectionId && x.IsActive).ToArrayAsync());
        Assert.Empty(await verify.CollectionRemittanceCoverages.AsNoTracking().Where(x => x.CollectionId == mistaken.CollectionId).ToArrayAsync());
        // Gross events 600, correction -200, corrected net 400, physical documents none, remitted cash 400: all distinct and consistent.
        Assert.Equal(600m, await verify.Collections.SumAsync(x => x.TotalAmount));
        Assert.Equal(-200m, await verify.CollectionCorrections.SumAsync(x => x.FinancialEffectAmount));
        Assert.Equal(400m, await verify.CollectionRemittances.SumAsync(x => x.RemittedAmount));
    }

    [SkippableTheory]
    [InlineData(FacilityCode.NCC)] [InlineData(FacilityCode.NPM)]
    public async Task Every_report_view_agrees_for_a_mixed_channel_month_with_a_reversal_and_a_report_only_adjustment(FacilityCode facility)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync(facility: facility);
        var today = PhilippineTime.Today; var period = c.Seed.Period;
        var officeReceipt = await HeadPostAsync(c, 600, await VersionAsync(c)); Assert.True(officeReceipt.IsSuccess, officeReceipt.Error);
        var collectorReceipt = await MobilePostAsync(c, 600, await VersionAsync(c)); Assert.Equal(SyncResultStatus.Synced, collectorReceipt.Status);
        await Task.Delay(50); var beforeCorrection = DateTime.UtcNow; await Task.Delay(50);
        await using (var ctx = db.CreateContext(c.Seed.TenantId))
        {
            var head = new HeadCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), c.Head, c.Tenant, new MutableTestClock(today));
            Assert.True((await head.ReverseAsync(new(Guid.NewGuid(), officeReceipt.Value!.CollectionId, "Wrong amount keyed"))).IsSuccess);
        }
        await using var read = db.CreateContext(c.Seed.TenantId);
        var reader = new CollectionActivityReader(read);

        // Collection Activity: each real collection once; gross, correction and corrected net stay distinct.
        var feed = await reader.GetAsync(c.Seed.TenantId, today, today);
        var canonical = feed.Where(x => x.Authority == "Canonical").ToArray();
        Assert.Equal((1200m, -600m, 600m), (canonical.Sum(x => x.Amount), canonical.Sum(x => x.CorrectionEffect), canonical.Sum(x => x.NetAmount)));
        Assert.Equal(2, canonical.Length);
        Assert.Equal(7m, feed.Where(x => x.Authority == "Legacy").Sum(x => x.NetAmount));   // the fixture independent legacy WCF payment, counted once and separately

        // Reconciliation bases: the correction was recorded after the cutoff, so As Originally Known keeps it out.
        var known = await reader.GetReconciliationAsync(c.Seed.TenantId, today, today, beforeCorrection, false);
        Assert.Equal(1200m, known.Sum(x => x.NetAmount)); Assert.All(known, x => Assert.Empty(x.Corrections));
        var latest = await reader.GetReconciliationAsync(c.Seed.TenantId, today, today, DateTime.UtcNow.AddMinutes(1), false);
        Assert.Equal((1200m, -600m, 600m), (latest.Sum(x => x.Amount), latest.Sum(x => x.CorrectionEffect), latest.Sum(x => x.NetAmount)));
        var activity = await reader.GetReconciliationAsync(c.Seed.TenantId, today, today, DateTime.UtcNow.AddMinutes(1), true);
        Assert.Equal(officeReceipt.Value!.ReferenceCode, Assert.Single(activity).ReferenceCode); Assert.Equal(-600m, activity.Single().Corrections.Single().FinancialEffect);
        Assert.Empty(await reader.GetReconciliationAsync(c.Seed.TenantId, today.AddDays(-1), today.AddDays(-1), DateTime.UtcNow.AddMinutes(1), false));   // date boundary

        // Official Monthly Income and source performance use the same cash basis; a Head adjustment moves only the official figure.
        var report = new GetOfficialMonthlyIncomeQueryHandler(read, new LegacyMonthlyIncomeReader(read), c.Head, c.Tenant, new ProofClock());
        var adjusted = await new ReportGovernanceWorkflow(read, c.Head, c.Tenant, new ProofClock(), new LegacyMonthlyIncomeReader(read))
            .AdjustAsync(new(Guid.NewGuid(), RentRowKey(c.Facility), period.Year, period.Month, 600, 654, "Report-only revision"));
        Assert.True(adjusted.IsSuccess, adjusted.Error);
        var statement = (await report.Handle(new(period.Year, period.Month), default)).Value!;
        var cell = statement.Groups.SelectMany(g => g.Rows).Single(r => r.Key == RentRowKey(c.Facility)).Months[period.Month - 1];
        Assert.Equal((0m, 600m, 600m, 54m, 654m), (cell.Legacy, cell.Canonical, cell.SystemAmount, cell.AdjustmentAmount, cell.OfficialAmount));
        var previous = period.AddMonths(-1);
        Assert.Equal(0m, (await report.Handle(new(previous.Year, previous.Month), default)).Value!.Groups.SelectMany(g => g.Rows).Single(r => r.Key == RentRowKey(c.Facility)).Months[previous.Month - 1].SystemAmount);
        var performance = (await new GetRevenueSourcePerformanceQueryHandler(report, read, c.Tenant).Handle(new(period.Year, period.Month), default)).Value!;
        var source = performance.Rows.Single(x => x.Key == RentRowKey(c.Facility));
        Assert.Equal((600m, 654m, 54m), (source.Collected, source.OfficialAmount, source.AdjustmentAmount));

        // Remittance and accountability: collector cash only, at corrected net; the adjustment and the Office receipt change nothing there.
        var position = (await Remit(read, c).GetPositionAsync(today, today)).Value!;
        Assert.Equal((600m, 0m, 600m), (position.Collected, position.Remitted, position.Unremitted));
        Assert.Equal((0m, 1), (position.OfficeOriginCollected, position.OfficeOriginCount));   // Office 600 received, 600 reversed
        var mine = (await new RemittanceWorkflow(read, new CollectorActor(c.Collector, c.Seed.TenantId), c.Tenant).GetMyCollectionsAsync(today, today)).Value;
        Assert.Equal([(collectorReceipt.ReferenceCode, 600m)], mine!.Select(x => (x.ReferenceCode, x.NetAmount)).ToArray());
        Assert.Equal(2, await read.Collections.CountAsync()); Assert.Empty(await read.CollectionRemittances.ToArrayAsync());
    }
}
