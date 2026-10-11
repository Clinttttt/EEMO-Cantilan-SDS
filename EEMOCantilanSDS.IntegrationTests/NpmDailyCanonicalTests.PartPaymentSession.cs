using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// An NPM part payment through the Mobile Collection Session: offered under one occupancy source for this month, quoted and recorded by the
/// server's own plan, replay-safe, and never mixed with another NPM payment of the same month in one checkout.
/// </summary>
public sealed partial class NpmDailyCanonicalTests
{
    private (CollectionSessionWorkflow Flow, CollectionSourceIdentity Identity, AppDbContext Ctx) SessionFor(World w, AppDbContext ctx, bool partEnabled = true)
    {
        var actor = Collector(w); var tenant = new FixedTenant(w.TenantId);
        var source = new CollectionSessionSources(ctx, actor, tenant, new Clock(), new DailySender(Record(ctx, w.TenantId, actor)),
            new NoMarketDays(), Whole(ctx, w, actor), Batch(ctx, w), partEnabled ? Part(ctx, w, actor) : null);
        var flow = new CollectionSessionWorkflow(new CollectionSessionStore(ctx), source, actor, tenant, new Clock(), NullLogger<CollectionSessionWorkflow>.Instance);
        var owner = ctx.Contracts.Single(x => x.StallId == w.StallA);
        return (flow, new CollectionSourceIdentity(SourceIdentityKind.Occupancy, owner.Id), ctx);
    }

    private static CollectionSessionIntent PartIntent(World w, CollectionSourceIdentity identity, decimal amount) =>
        new(Guid.NewGuid(), Today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.NpmPartPayment, amount, NpmPart: new(w.StallA, Today.Year, Today.Month))], SourceIdentity: identity);

    [SkippableFact]
    public async Task Session_PartPayment_IsOfferedWithTheServersMaximum_QuotedByThePlan_AndRecordedExactlyOnce()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        await using var ctx = db.CreateContext(w.TenantId);
        var (flow, identity, _) = SessionFor(w, ctx);

        var discovery = (await flow.DiscoverNativeAsync(identity)).Value!;
        var offer = discovery.Operations.Single(x => x.Kind == CollectionSessionItemKind.NpmPartPayment);
        var choice = Assert.Single(offer.Choices!);
        Assert.Equal((CollectionSessionAmountRule.DirectAmount, true), (choice.AmountRule, choice.CanEnterAmount));
        Assert.Equal(Math.Min(900m, Today.Day * 30m), choice.MaximumAmount);       // the month's own balance today, as the server states it
        Assert.Null(choice.ServerAmount);                                          // the amount is the payor's, never a client-side figure
        Assert.Contains(discovery.Operations, x => x.Kind == CollectionSessionItemKind.NpmDaily);   // the existing choices are untouched

        var intent = PartIntent(w, identity, 20m);
        var quote = (await flow.QuoteAsync(intent)).Value!;
        Assert.True(quote.CanRecord, string.Join(";", quote.Problems.Select(p => p.Message)));
        var item = Assert.Single(quote.Items);
        Assert.Equal((20m, "NPM_PART_PAYMENT"), (item.Amount, item.OperationCode));
        Assert.Contains("Partial", item.Context);
        Assert.Equal(20m, quote.GrandTotal);

        var recorded = (await flow.RecordAsync(new(intent, quote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.Recorded, recorded.Status);
        Assert.Equal(20m, Assert.Single(recorded.Collections).Amount);
        Assert.StartsWith("SRC-", recorded.Collections[0].ReferenceCode);
        var replay = (await flow.RecordAsync(new(intent, "lost response"))).Value!;     // the response was lost: the same session is the same outcome
        Assert.Equal(recorded.Collections[0].CollectionId, replay.Collections[0].CollectionId);

        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.Collections.CountAsync());
        var day = await ctx.DailyCollections.SingleAsync();
        Assert.Equal((false, 20m, 10m), (day.IsPaid, day.CanonicalSettledAmount, day.RemainingBaseFee));
        Assert.Equal(20m, await IncomeAsync(w, "RENT_NPM"));
    }

    [SkippableFact]
    public async Task Session_PartPayment_RefusesMoreThanTheMonthOwes_AndNeverMixesWithAnotherNpmPaymentOfTheMonth()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        await using var ctx = db.CreateContext(w.TenantId);
        var (flow, identity, _) = SessionFor(w, ctx);
        var maximum = Math.Min(900m, Today.Day * 30m);

        var over = (await flow.QuoteAsync(PartIntent(w, identity, maximum + 0.01m))).Value!;
        Assert.False(over.CanRecord);
        Assert.Equal("AmountExceedsBalance", Assert.Single(over.Problems).Code);
        Assert.False((await flow.RecordAsync(new(PartIntent(w, identity, maximum + 0.01m), "x"))).Value!.Status == CollectionSessionStatus.Recorded);
        Assert.Equal(0, await ctx.Collections.CountAsync());

        var withWhole = PartIntent(w, identity, 20m);
        withWhole = withWhole with { Items = [withWhole.Items[0], new(Guid.NewGuid(), CollectionSessionItemKind.NpmWholePayment, 0m, NpmWhole: new(w.StallA, Today.Year, Today.Month))] };
        Assert.Contains((await flow.QuoteAsync(withWhole)).Value!.Problems, x => x.Code == "DuplicateBusinessEvent");
        var withDaily = PartIntent(w, identity, 20m);
        withDaily = withDaily with { Items = [withDaily.Items[0], new(Guid.NewGuid(), CollectionSessionItemKind.NpmDaily, 30m, NpmDaily: new(w.StallA))] };
        Assert.Contains((await flow.QuoteAsync(withDaily)).Value!.Problems, x => x.Code == "DuplicateBusinessEvent");

        var direct = PartIntent(w, identity, 20m) with { SourceIdentity = null, PayerSnapshot = "Walk Up" };   // never a direct, source-less collection
        Assert.False((await flow.QuoteAsync(direct)).Value!.CanRecord);
    }

    [SkippableFact]
    public async Task Session_PartPayment_IsNotOfferedWhenThePartPaymentSwitchIsOff()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        await using var ctx = db.CreateContext(w.TenantId);
        var actor = Collector(w); var tenant = new FixedTenant(w.TenantId);
        // The workflow exists but its switch is off: the same checkout must offer nothing new.
        var off = Part(ctx, w, actor, enabled: false);
        var source = new CollectionSessionSources(ctx, actor, tenant, new Clock(), new DailySender(Record(ctx, w.TenantId, actor)), new NoMarketDays(), Whole(ctx, w, actor), Batch(ctx, w), off);
        var flow = new CollectionSessionWorkflow(new CollectionSessionStore(ctx), source, actor, tenant, new Clock(), NullLogger<CollectionSessionWorkflow>.Instance);
        var identity = new CollectionSourceIdentity(SourceIdentityKind.Occupancy, (await ctx.Contracts.SingleAsync(x => x.StallId == w.StallA)).Id);

        var discovery = (await flow.DiscoverNativeAsync(identity)).Value!;

        Assert.DoesNotContain(discovery.Operations, x => x.Kind == CollectionSessionItemKind.NpmPartPayment);
        Assert.Contains(discovery.Operations, x => x.Kind == CollectionSessionItemKind.NpmDaily);
        Assert.False((await flow.QuoteAsync(PartIntent(w, identity, 20m))).Value!.CanRecord);
    }
}
