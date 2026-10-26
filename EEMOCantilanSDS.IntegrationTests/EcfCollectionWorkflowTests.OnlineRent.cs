using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

// ST-17: a settled online monthly Stall Rent payment on a Canonical row creates exactly one canonical Collection/SRC.
public sealed partial class EcfCollectionWorkflowTests
{
    private sealed class QuietNotifier : IOnlinePaymentNotifier
    {
        public Task NotifyPaymentReceivedAsync(OnlinePaymentNotification n, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class QuietCache : IEemoCacheInvalidator
    {
        public Task InvalidateRegionAsync(string region, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string t, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string t, FacilityCode f, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string t, FacilityCode? f, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTenantContext : ITenantContext { public string TenantCode => "ecf-test"; }

    private static OnlinePaymentSettlementService Settler(AppDbContext ctx) =>
        new(new PaymentRepository(ctx), null!, null!, null!, new QuietNotifier(), new UnitOfWork(ctx), new QuietCache(),
            new StubTenantContext(), null, new OnlineRentCanonicalPoster(ctx));

    private static PaymentGatewayEvent PaidEvent(OnlinePaymentTransaction tx, decimal? amount = null) =>
        new(PaymentGatewayEventType.Paid, tx.GatewayReference!, amount ?? tx.Amount, "pay_test_1", "gcash", DateTime.UtcNow, "{}");

    private async Task<Guid> SeedOnlineTransactionAsync(Guid tenantId, Guid recordId, decimal amount)
    {
        await using var ctx = db.CreateContext(tenantId);
        var tx = OnlinePaymentTransaction.Create("EEMO-OP-" + Guid.NewGuid().ToString("N")[..14].ToUpperInvariant(),
            Guid.NewGuid(), recordId, amount, "PayMongo");
        tx.SetPending("cs_" + tx.Id.ToString("N")[..12], "https://pay.test/" + tx.Id);
        ctx.OnlinePaymentTransactions.Add(tx);
        await ctx.SaveChangesAsync();
        return tx.Id;
    }

    private async Task<Result<bool>> SettleAsync(Guid tenantId, Guid txId, decimal? amount = null)
    {
        await using var ctx = db.CreateContext(tenantId);
        var tx = await ctx.OnlinePaymentTransactions.SingleAsync(x => x.Id == txId);
        return await Settler(ctx).SettleAsync(tx, PaidEvent(tx, amount));
    }

    private async Task<(Seed Seed, RentSeed Rent, Guid Tx)> OnlineRentAsync(
        decimal rate = 1200m, decimal opening = 0m, bool canonical = true, decimal? checkout = null, FacilityCode facility = FacilityCode.TCC)
    {
        await db.ResetAsync();
        var seed = await SeedEcfAsync(true);
        var rent = await SeedRentSourcesAsync(seed, [seed.Period], canonical, partialAmount: opening, facilityCode: facility,
            monthlyRate: rate, sourceOwned: false);
        var tx = await SeedOnlineTransactionAsync(seed.TenantId, rent.PaymentRecordIds[seed.Period], checkout ?? (rate - opening));
        return (seed, rent, tx);
    }

    [SkippableFact]
    public async Task Online_canonical_rent_payment_posts_exactly_one_SRC_and_every_reader_shows_one_money_event()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (seed, rent, txId) = await OnlineRentAsync();

        var settled = await SettleAsync(seed.TenantId, txId);
        Assert.True(settled.IsSuccess, settled.Error);

        await using var ctx = db.CreateContext(seed.TenantId);
        var collection = await ctx.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync();
        Assert.Equal(1200m, collection.TotalAmount);
        Assert.Null(collection.CollectorId);
        Assert.Equal(seed.PayorId, collection.PayorId);                       // only the contract's explicit Payor link
        var tx = await ctx.OnlinePaymentTransactions.SingleAsync();
        Assert.Equal(tx.PayorUserId.ToString("N"), collection.ActorId);          // auditable actor = the paying portal user
        Assert.Equal("Payor", collection.ActorRole);
        var line = Assert.Single(collection.Lines);
        Assert.Equal(1200m, line.Amount);
        var allocation = Assert.Single(line.Allocations);
        Assert.Equal((CollectionSourceKind.PaymentRecord, rent.PaymentRecordIds[seed.Period]), (allocation.SourceKind, allocation.SourceId));
        var op = await ctx.PostingOperations.SingleAsync();
        Assert.Equal(OnlineRentCanonicalPoster.OperationIdFor(tx.Id), op.ClientOperationId);   // SRC reachable from the transaction
        Assert.Equal(collection.Id, op.CollectionId);
        Assert.Equal(OnlineRentCanonicalPoster.Origin, op.Origin);
        Assert.Contains(tx.PaymentId!, op.NormalizedIntent);                    // PayMongo id correlation is persisted
        Assert.Empty(await ctx.AccountableDocuments.Where(x => x.CollectionId != null).ToArrayAsync());   // OR stays separate
        Assert.Equal(PaymentStatus.Paid, (await ctx.PaymentRecords.SingleAsync()).Status);
        Assert.Equal(OnlinePaymentStatus.Paid, tx.Status);

        // Awaiting OR queue still lists it (not treated as unpaid, not lost), and staff can complete it without touching the canonical row.
        var awaiting = Assert.Single(await new OnlinePaymentRepository(ctx).GetAwaitingOrAsync());
        Assert.Equal(1200m, awaiting.Amount);

        // Collection Activity: one event, the SRC; the converted row's legacy projection is not a second event.
        var events = await new CollectionActivityReader(ctx).GetAsync(seed.TenantId, PhilippineTime.Today, PhilippineTime.Today);
        var only = Assert.Single(events.Where(e => e.Source != "UtilityBill"));   // the seed's own legacy water bill is a different source
        Assert.Equal("Canonical", only.Authority); Assert.Equal(collection.ReferenceCode, only.ReferenceCode); Assert.Equal(1200m, only.Amount);

        // Official Monthly Income: canonical 1200, legacy 0 - not counted twice.
        var actor = new TestActor(seed.UserId, seed.TenantId, "SuperAdmin");
        var statement = (await new GetOfficialMonthlyIncomeQueryHandler(ctx, new LegacyMonthlyIncomeReader(ctx), actor,
            new FixedTenant(seed.TenantId), new ProofClock()).Handle(new(PhilippineTime.Today.Year, PhilippineTime.Today.Month), default)).Value!;
        var cell = statement.Groups.SelectMany(g => g.Rows).Single(r => r.Key == "RENT_TCC").Months[PhilippineTime.Today.Month - 1];
        Assert.Equal((0m, 1200m), (cell.Legacy, cell.Canonical));

        // Staff OR on the transaction does not write the legacy mirror of a canonical row.
        tx.CompleteWithOr("OR-ONLINE-1", "head"); await ctx.SaveChangesAsync();
        Assert.Empty(await new OnlinePaymentRepository(ctx).GetAwaitingOrAsync());
        Assert.Null((await ctx.PaymentRecords.SingleAsync()).ORNumber);
    }

    [SkippableFact]
    public async Task Replayed_webhook_and_a_stale_second_settle_resolve_to_the_same_SRC()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (seed, _, txId) = await OnlineRentAsync();
        await using var first = db.CreateContext(seed.TenantId); await using var stale = db.CreateContext(seed.TenantId);
        var a = await first.OnlinePaymentTransactions.SingleAsync(); var b = await stale.OnlinePaymentTransactions.SingleAsync();   // both still Pending
        Assert.True((await Settler(first).SettleAsync(a, PaidEvent(a))).IsSuccess);
        Assert.True((await Settler(stale).SettleAsync(b, PaidEvent(b))).IsSuccess);                 // lost the race: prior outcome
        Assert.True((await SettleAsync(seed.TenantId, txId)).IsSuccess);                              // plain replay: settled no-op
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Single(await verify.Collections.ToArrayAsync()); Assert.Single(await verify.PostingOperations.ToArrayAsync());
        Assert.Equal(1200m, await verify.Collections.SumAsync(x => x.TotalAmount));
    }

    [SkippableFact]
    public async Task Concurrent_webhook_and_confirm_post_one_Collection()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (seed, _, txId) = await OnlineRentAsync();
        await using var left = db.CreateContext(seed.TenantId); await using var right = db.CreateContext(seed.TenantId);
        var a = await left.OnlinePaymentTransactions.SingleAsync(); var b = await right.OnlinePaymentTransactions.SingleAsync();
        async Task<bool> Run(AppDbContext c, OnlinePaymentTransaction t)
        {
            try { return (await Settler(c).SettleAsync(t, PaidEvent(t))).IsSuccess; }
            catch (DbUpdateConcurrencyException) { return true; }      // the handlers treat the lost write as already settled
        }
        var results = await Task.WhenAll(Run(left, a), Run(right, b));
        Assert.All(results, Assert.True);
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Single(await verify.Collections.ToArrayAsync()); Assert.Single(await verify.PostingOperations.ToArrayAsync());
        Assert.Equal(OnlinePaymentStatus.Paid, (await verify.OnlinePaymentTransactions.SingleAsync(x => x.Id == txId)).Status);
    }

    [SkippableFact]
    public async Task Amount_mismatch_conflicts_and_posts_nothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (seed, _, txId) = await OnlineRentAsync();
        var result = await SettleAsync(seed.TenantId, txId, amount: 1000m);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Empty(await verify.Collections.ToArrayAsync());
        Assert.Equal(OnlinePaymentStatus.Pending, (await verify.OnlinePaymentTransactions.SingleAsync()).Status);
    }

    [SkippableFact]
    public async Task A_legacy_partial_on_a_canonical_row_is_kept_for_reconciliation_and_posts_nothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (seed, _, txId) = await OnlineRentAsync(opening: 600m);                 // the row carries 600 of LEGACY partial evidence: same guard as the Mobile rent path
        var result = await SettleAsync(seed.TenantId, txId);
        Assert.Equal(ResultStatus.Conflict, result.Status); Assert.Contains("RECONCILIATION_REQUIRED", result.Error);
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Empty(await verify.Collections.ToArrayAsync());
        Assert.Equal(OnlinePaymentStatus.ReconciliationRequired, (await verify.OnlinePaymentTransactions.SingleAsync()).Status);   // the capture is preserved, not lost
    }

    [SkippableFact]
    public async Task Overpayment_beyond_the_rent_outstanding_is_kept_for_reconciliation()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (seed, _, txId) = await OnlineRentAsync(checkout: 1500m);
        var result = await SettleAsync(seed.TenantId, txId);
        Assert.Equal(ResultStatus.Conflict, result.Status); Assert.Contains("RECONCILIATION_REQUIRED", result.Error);
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Empty(await verify.Collections.ToArrayAsync());
        Assert.Equal(OnlinePaymentStatus.ReconciliationRequired, (await verify.OnlinePaymentTransactions.SingleAsync()).Status);
        Assert.NotEqual(PaymentStatus.Paid, (await verify.PaymentRecords.SingleAsync()).Status);       // no wrong clear
    }

    [SkippableTheory]
    [InlineData(600, "partial")]
    [InlineData(1200, "full")]
    public async Task A_collector_posting_first_leaves_the_online_capture_for_reconciliation_and_the_collector_SRC_untouched(decimal collectorPaid, string why)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (seed, rent, txId) = await OnlineRentAsync();
        var collector = await SeedCollectorAsync(seed.TenantId, FacilityCode.TCC);
        Guid collectorSrc;
        await using (var mobileDb = db.CreateContext(seed.TenantId))
        {
            var quote = (await new CollectionComposerWorkflow(mobileDb, new TestActor(seed.UserId, seed.TenantId), new FixedTenant(seed.TenantId))
                .GetRentObligationAsync(rent.StallId, seed.Period.Year, seed.Period.Month)).Value!;
            var posted = await AsCollector(mobileDb, seed.TenantId, collector).PostMobileRentAsync(new(Guid.NewGuid(), rent.StallId,
                seed.Period.Year, seed.Period.Month, collectorPaid, quote.SourceVersion, PhilippineTime.Today));
            Assert.True(posted.IsSuccess, posted.Error); collectorSrc = posted.Value!.CollectionId;
        }

        var result = await SettleAsync(seed.TenantId, txId);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        await using var verify = db.CreateContext(seed.TenantId);
        var only = await verify.Collections.SingleAsync();
        Assert.Equal((collectorSrc, collectorPaid), (only.Id, only.TotalAmount)); Assert.Equal(collector, only.CollectorId);
        Assert.Equal(OnlinePaymentStatus.ReconciliationRequired, (await verify.OnlinePaymentTransactions.SingleAsync()).Status);
        Assert.Single(await verify.PostingOperations.ToArrayAsync());
    }

    [SkippableFact]
    public async Task Legacy_authority_row_keeps_MarkPaidOnline_and_creates_no_Collection()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (seed, _, txId) = await OnlineRentAsync(canonical: false);
        Assert.True((await SettleAsync(seed.TenantId, txId)).IsSuccess);
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Empty(await verify.Collections.ToArrayAsync()); Assert.Empty(await verify.PostingOperations.ToArrayAsync());
        var record = await verify.PaymentRecords.SingleAsync();
        Assert.Equal(PaymentStatus.Paid, record.Status); Assert.Equal(SettlementAuthority.Legacy, record.SettlementAuthorityState);
        Assert.Equal(OnlinePaymentStatus.Paid, (await verify.OnlinePaymentTransactions.SingleAsync()).Status);
    }

    [SkippableFact]
    public async Task A_transaction_pointing_at_another_tenants_record_conflicts_and_posts_nothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var a = await SeedEcfAsync(true); var b = await SeedEcfAsync(true);
        var rentB = await SeedRentSourcesAsync(b, [b.Period], true, facilityCode: FacilityCode.TCC, monthlyRate: 1200m);
        var txId = await SeedOnlineTransactionAsync(a.TenantId, rentB.PaymentRecordIds[b.Period], 1200m);   // tenant A transaction, tenant B record
        var result = await SettleAsync(a.TenantId, txId);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        await using var verifyB = db.CreateContext(b.TenantId);
        Assert.Empty(await verifyB.Collections.ToArrayAsync());
        Assert.NotEqual(PaymentStatus.Paid, (await verifyB.PaymentRecords.SingleAsync()).Status);
    }

    [SkippableFact]
    public async Task Checkout_quote_for_a_clean_canonical_row_is_its_rent_outstanding_and_one_with_legacy_components_is_paused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (seed, _, _) = await OnlineRentAsync();
        await using (var ctx = db.CreateContext(seed.TenantId))
        {
            var record = await ctx.PaymentRecords.AsNoTracking().SingleAsync();
            var quote = await new OnlineRentCanonicalPoster(ctx).QuoteAsync(record, default);
            Assert.True(quote.Payable, quote.Reason); Assert.Equal(1200m, quote.Amount);
        }

        var (partialSeed, _, _) = await OnlineRentAsync(opening: 600m);
        await using var partialCtx = db.CreateContext(partialSeed.TenantId);
        var mixed = await new OnlineRentCanonicalPoster(partialCtx).QuoteAsync(await partialCtx.PaymentRecords.AsNoTracking().SingleAsync(), default);
        Assert.False(mixed.Payable); Assert.Contains("reconciliation", mixed.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
