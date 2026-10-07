using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Fees;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;
public sealed partial class NpmDailyCanonicalTests
{
    private sealed class NoMarketDays : ITpmMarketDayProvider
    {
        public Task<DayOfWeek> GetMarketDayAsync(DateOnly asOf, CancellationToken ct = default) => Task.FromResult(DayOfWeek.Friday);
        public Task<IReadOnlyList<DateOnly>> GetMarketDatesAsync(int year, int month, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<DateOnly>>([]);
    }
    [SkippableFact]
    public async Task Source_native_NPM_offers_daily_and_whole_and_daily_posts_the_existing_writer_exactly_once()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? ""); await db.ResetAsync(); var w = await SeedAsync(true);
        await using var ctx = db.CreateContext(w.TenantId);
        var actor = Collector(w); var tenant = new FixedTenant(w.TenantId);
        var source = new CollectionSessionSources(ctx, actor, tenant, new Clock(), new DailySender(Record(ctx, w.TenantId, actor)),
            new NoMarketDays(), Whole(ctx, w, actor), Batch(ctx, w));
        var flow = new CollectionSessionWorkflow(new CollectionSessionStore(ctx), source, actor, tenant, new Clock(), NullLogger<CollectionSessionWorkflow>.Instance);
        var owner = await ctx.Contracts.SingleAsync(x => x.StallId == w.StallA);
        var identity = new CollectionSourceIdentity(SourceIdentityKind.Occupancy, owner.Id);
        var discovery = (await flow.DiscoverNativeAsync(identity)).Value!;
        var daily = Assert.Single(discovery.Operations.Single(x => x.Kind == CollectionSessionItemKind.NpmDaily).Choices!);
        Assert.True(discovery.Operations.Single(x => x.Kind == CollectionSessionItemKind.NpmDaily).CanAutoSelect);
        Assert.Contains(discovery.Operations, x => x.Kind == CollectionSessionItemKind.NpmWholePayment);
        var authoritative = (await Batch(ctx, w).SourcesAsync()).Value!.Single(x => x.StallId == w.StallA);
        Assert.Equal(authoritative.EffectiveCharge, daily.ServerAmount);
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.NpmDaily, daily.ServerAmount!.Value, NpmDaily: new(w.StallA))], SourceIdentity: identity);
        Assert.Equal("AmountChanged", Assert.Single((await flow.QuoteAsync(intent with { Items = [intent.Items[0] with { ConfirmedAmount = 1m }] })).Value!.Problems).Code);
        var both = intent with { Items = [intent.Items[0], new(Guid.NewGuid(), CollectionSessionItemKind.NpmWholePayment, 0m, NpmWhole: new(w.StallA, Today.Year, Today.Month))] };
        Assert.Contains((await flow.QuoteAsync(both)).Value!.Problems, x => x.Code == "DuplicateBusinessEvent");
        var quote = (await flow.QuoteAsync(intent)).Value!; Assert.True(quote.CanRecord);
        var recorded = (await flow.RecordAsync(new(intent, quote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.Recorded, recorded.Status); Assert.StartsWith("SRC-", Assert.Single(recorded.Collections).ReferenceCode);
        Assert.Equal(recorded.Collections[0].CollectionId, (await flow.RecordAsync(new(intent, "lost response"))).Value!.Collections[0].CollectionId);
        Assert.Single(await ctx.Collections.ToListAsync()); Assert.True((await ctx.DailyCollections.SingleAsync()).IsPaid);
        Assert.DoesNotContain((await flow.DiscoverNativeAsync(identity)).Value!.Operations, x => x.Kind == CollectionSessionItemKind.NpmDaily);
        Assert.Contains((await flow.DiscoverNativeAsync(identity)).Value!.Operations, x => x.Kind == CollectionSessionItemKind.NpmWholePayment);
    }
    [SkippableFact]
    public async Task Four_of_five_paid_leaves_CollectAll_ready_and_only_the_pending_stall_posts()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? ""); await db.ResetAsync(); var w = await SeedAsync(true);
        await using var ctx = db.CreateContext(w.TenantId);
        var facility = await ctx.Facilities.SingleAsync(x => x.Code == FacilityCode.NPM);
        foreach (var number in new[] { "V-03", "V-04" })
        {
            var stall = Stall.Create(facility.Id, number, 900m, ApplicableFees.BaseRental, MarketSection.VegetableArea, municipalityId: w.TenantId);
            ctx.AddRange(stall, Contract.Create(stall.Id, number, number, new(Today.Year, Today.Month, 1), 5, 900m, createdBy: "test"));
        }
        await ctx.SaveChangesAsync();
        var all = (await Batch(ctx, w).SourcesAsync()).Value!;
        Assert.Equal(5, all.Count);
        foreach (var item in all.Take(4)) Assert.True((await Record(ctx, w.TenantId, Collector(w)).Handle(new(item.StallId, Today, true, ClientOperationId: Guid.NewGuid()), default)).IsSuccess);
        var readiness = (await Batch(ctx, w).ReadinessAsync()).Value!;
        Assert.True(readiness.CanCollectAll); Assert.Equal(1, readiness.EligibleCount); Assert.Equal(4, readiness.AlreadyCollectedCount);
        var pending = Assert.Single(readiness.Sources.Where(x => x.CanCollect));
        var intent = new NpmDailyBatchIntent(Guid.NewGuid(), Today, [new(Guid.NewGuid(), pending.StallId)]);
        var quote = (await Batch(ctx, w).PreviewAsync(intent)).Value!;
        Assert.True(quote.CanRecord); Assert.Equal(pending.EffectiveCharge, quote.Total);
        var posted = (await Batch(ctx, w).RecordAsync(new(intent, quote.QuoteFingerprint!))).Value!;
        Assert.Single(posted.Collections); Assert.Equal(5, await ctx.Collections.CountAsync());
        Assert.False((await Batch(ctx, w).ReadinessAsync()).Value!.CanCollectAll);
    }
    [SkippableFact]
    public async Task Mobile_daily_remove_restores_source_balance_and_edit_cannot_supply_arbitrary_daily_money()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? ""); await db.ResetAsync(); var w = await SeedAsync(true);
        await using var ctx = db.CreateContext(w.TenantId); var actor = Collector(w); var tenant = new FixedTenant(w.TenantId);
        var source = new CollectionSessionSources(ctx, actor, tenant, new Clock(), new DailySender(Record(ctx, w.TenantId, actor)),
            new NoMarketDays(), Whole(ctx, w, actor), Batch(ctx, w));
        var sessions = new CollectionSessionWorkflow(new CollectionSessionStore(ctx), source, actor, tenant, new Clock(), NullLogger<CollectionSessionWorkflow>.Instance);
        var corrections = new MobileCollectionCorrectionWorkflow(ctx, new CollectionSessionStore(ctx), source, sessions, new CollectionActivityReader(ctx),
            new OfficeCollectionWorkflow(ctx, actor, tenant, new Clock(), new FeeRateResolver(ctx)), actor, tenant, new Clock());
        var monthlyBefore = (await Whole(ctx, w, actor).QuoteAsync(w.StallA, Today.Year, Today.Month)).Value!.Amount;
        Assert.True((await Record(ctx, w.TenantId, actor).Handle(new(w.StallA, Today, true, ClientOperationId: Guid.NewGuid()), default)).IsSuccess);
        var original = await ctx.Collections.SingleAsync(); var day = await ctx.DailyCollections.SingleAsync();
        var recent = Assert.Single((await corrections.RecentAsync()).Value!.Rows);
        Assert.True(recent.CanEdit); Assert.Equal(CollectionSessionItemKind.NpmDaily, recent.EditSource!.Kind);
        Assert.Equal(w.StallA, recent.EditSource.Identity.StallId);
        Assert.True(day.IsPaid); var charge = day.DailyFee;
        Assert.Equal(monthlyBefore - charge, (await Whole(ctx, w, actor).QuoteAsync(w.StallA, Today.Year, Today.Month)).Value!.Amount);
        var occupancy = await ctx.Contracts.SingleAsync(x => x.StallId == w.StallA); var operation = Guid.NewGuid();
        var intent = new EditMobileCollectionIntent(operation, original.Id, new(MobileCorrectionReason.WrongAmount),
            new(operation, Today, null, [new(Guid.NewGuid(), CollectionSessionItemKind.NpmDaily, 1m, NpmDaily: new(w.StallA))],
                SourceIdentity: new(SourceIdentityKind.Occupancy, occupancy.Id)));
        var badQuote = await corrections.QuoteEditAsync(intent); Assert.True(badQuote.IsSuccess, badQuote.Error);
        Assert.Contains(badQuote.Value!.Problems, x => x.Code == "AmountChanged");
        ctx.ChangeTracker.Clear(); Assert.True((await ctx.DailyCollections.SingleAsync()).IsPaid); Assert.Empty(await ctx.CollectionCorrections.ToListAsync());
        intent = intent with { Replacement = intent.Replacement with { Items = [intent.Replacement.Items[0] with { ConfirmedAmount = charge }] } };
        var quote = (await corrections.QuoteEditAsync(intent)).Value!; Assert.True(quote.CanRecord);
        var result = await corrections.EditAsync(new(intent, quote.QuoteFingerprint!)); Assert.True(result.IsSuccess, result.Error);
        ctx.ChangeTracker.Clear();
        Assert.True((await ctx.DailyCollections.SingleAsync()).IsPaid);
        Assert.Equal(result.Value!.Replacement!.CollectionId, (await ctx.DailyCollections.SingleAsync()).CanonicalCollectionId);
        Assert.Equal(monthlyBefore - charge, (await Whole(ctx, w, actor).QuoteAsync(w.StallA, Today.Year, Today.Month)).Value!.Amount);
        Assert.True((await corrections.RemoveAsync(new(Guid.NewGuid(), result.Value.Replacement.CollectionId, new(MobileCorrectionReason.EnteredByMistake)))).IsSuccess);
        ctx.ChangeTracker.Clear(); Assert.False((await ctx.DailyCollections.SingleAsync()).IsPaid);
        Assert.Equal(monthlyBefore, (await Whole(ctx, w, actor).QuoteAsync(w.StallA, Today.Year, Today.Month)).Value!.Amount);
        Assert.Equal(2, await ctx.Collections.CountAsync()); Assert.Equal(0m, (await new CollectionActivityReader(ctx).GetAsync(w.TenantId, Today, Today)).Sum(x => x.NetAmount));
    }
}
