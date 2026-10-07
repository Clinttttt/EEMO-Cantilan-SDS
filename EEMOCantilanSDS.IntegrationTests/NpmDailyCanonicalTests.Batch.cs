using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;
public sealed partial class NpmDailyCanonicalTests
{
    private sealed class DailySender(RecordDailyCollectionCommandHandler handler, int failAt = 0) : ISender
    {
        private int calls;
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (++calls == failAt) throw new InvalidOperationException("Injected child failure");
            if (request is not RecordDailyCollectionCommand daily) throw new NotSupportedException();
            return (TResponse)(object)await handler.Handle(daily, ct);
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private NpmDailyBatchWorkflow Batch(AppDbContext ctx, World w, int failAt = 0) => new(ctx, new CollectionSessionStore(ctx),
        Poster(ctx, w.TenantId, Collector(w)), Settlement(ctx), new FeeRateResolver(ctx), new DailySender(Record(ctx, w.TenantId, Collector(w)), failAt),
        Collector(w), new FixedTenant(w.TenantId), new Clock());

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Collect_all_posts_only_checked_today_stalls_with_independent_SRCs_and_atomic_replay(bool failAfterFirst)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? ""); await db.ResetAsync(); var w = await SeedAsync(canonical: true);
        var intent = new NpmDailyBatchIntent(Guid.NewGuid(), Today, [new(Guid.NewGuid(), w.StallA), new(Guid.NewGuid(), w.StallB)]);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var batch = Batch(ctx, w, failAfterFirst ? 2 : 0);
            var all = (await batch.SourcesAsync()).Value!; Assert.Equal(3, all.Count(x => x.CanCollect));
            var quote = (await batch.PreviewAsync(intent)).Value!; Assert.True(quote.CanRecord); Assert.Equal(2, quote.ItemCount);
            Assert.Equal(quote.Sources.Sum(x => x.EffectiveCharge), quote.Total); Assert.Empty(await ctx.Collections.ToListAsync());
            if (failAfterFirst)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => batch.RecordAsync(new(intent, quote.QuoteFingerprint!)));
                Assert.Empty(await ctx.Collections.ToListAsync()); Assert.Empty(await ctx.DailyCollections.ToListAsync());
            }
        }
        await using var retry = db.CreateContext(w.TenantId); var flow = Batch(retry, w);
        var fresh = (await flow.PreviewAsync(intent)).Value!;
        var posted = await flow.RecordAsync(new(intent, fresh.QuoteFingerprint!)); Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(2, posted.Value!.Collections.Count); Assert.Equal(2, posted.Value.Collections.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.Equal(fresh.Total, posted.Value.GrandTotal);
        var replay = (await flow.RecordAsync(new(intent with { Items = intent.Items.Reverse().ToArray() }, "response lost"))).Value!;
        Assert.True(replay.ExistingOutcome); Assert.Equal(posted.Value.Collections.Select(x => x.CollectionId), replay.Collections.Select(x => x.CollectionId));
        Assert.Equal("SessionIntentConflict", (await flow.RecordAsync(new(intent with { Items = [intent.Items[0]] }, "old"))).Error);
        Assert.Equal(2, await retry.DailyCollections.CountAsync()); Assert.DoesNotContain(await retry.DailyCollections.ToListAsync(), x => x.StallId == w.FishStall || x.IsAbsent);
        var another = intent with { ClientCollectionSessionId = Guid.NewGuid() };
        Assert.False((await flow.PreviewAsync(another)).Value!.CanRecord); Assert.Equal(2, await retry.Collections.CountAsync());
    }
    [SkippableFact]
    public async Task Collect_all_changed_rate_or_settled_day_refuses_old_quote_without_money()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? ""); await db.ResetAsync(); var w = await SeedAsync(canonical: true);
        await using var ctx = db.CreateContext(w.TenantId); var flow = Batch(ctx, w);
        var intent = new NpmDailyBatchIntent(Guid.NewGuid(), Today, [new(Guid.NewGuid(), w.StallA), new(Guid.NewGuid(), w.StallB)]);
        var quote = (await flow.PreviewAsync(intent)).Value!;
        var rate = await ctx.FacilityRates.SingleAsync(x => x.RateKey == EEMOCantilanSDS.Domain.Enums.FeeRateKey.NpmDailyStall);
        rate.UpdateAmount(rate.Amount + 1m, "head"); await ctx.SaveChangesAsync();
        Assert.Equal("QuoteStale", (await flow.RecordAsync(new(intent, quote.QuoteFingerprint!))).Error);
        Assert.Empty(await ctx.Collections.ToListAsync()); Assert.Empty(await ctx.DailyCollections.ToListAsync());
    }
}
