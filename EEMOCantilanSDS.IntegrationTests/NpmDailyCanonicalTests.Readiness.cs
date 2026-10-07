using System.Net.Http.Json;
using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Requests.Mobile;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class NpmDailyCanonicalTests
{
    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Http_readiness_agrees_with_ordinary_daily_for_five_canonical_stalls(int paid)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync(); var w = await SeedAsync(true);
        await using var ctx = db.CreateContext(w.TenantId);
        var facility = await ctx.Facilities.SingleAsync(x => x.Code == FacilityCode.NPM);
        foreach (var number in new[] { "V-03", "V-04" })
        {
            var stall = Stall.Create(facility.Id, number, 900m, ApplicableFees.BaseRental, MarketSection.VegetableArea, municipalityId: w.TenantId);
            ctx.AddRange(stall, Contract.Create(stall.Id, number, number, new(Today.Year, Today.Month, 1), 5, 900m, createdBy: "test"));
        }
        // An empty space is not a pending payer/stall on the ordinary Daily round.
        ctx.Add(Stall.Create(facility.Id, "Vacant", 900m, ApplicableFees.BaseRental, MarketSection.VegetableArea, municipalityId: w.TenantId));
        await ctx.SaveChangesAsync();
        var stalls = (await Batch(ctx, w).ReadinessAsync()).Value!.Sources;
        await using var host = await CollectorApiHost.StartAsync(db, w.CollectorId, w.TenantId);
        foreach (var stall in stalls.Take(paid))
        {
            var response = await host.Client.PostAsJsonAsync("/api/mobile/npm/collections/record",
                new RecordMobileNpmCollectionRequest(stall.StallId, true, CollectionDate: Today), CollectorApiHost.Json);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        var readiness = (await host.Client.GetFromJsonAsync<NpmDailyBatchReadiness>("/api/mobile/npm-daily-batch/readiness", CollectorApiHost.Json))!;
        var daily = (await host.Client.GetFromJsonAsync<MobileNpmCollectionDto>($"/api/mobile/npm/collections?year={Today.Year}&month={Today.Month}", CollectorApiHost.Json))!;
        Assert.Equal(5, readiness.Sources.Count);
        Assert.Equal(5 - paid, readiness.EligibleCount);
        Assert.Equal(paid, readiness.AlreadyCollectedCount);
        Assert.Equal(paid < 5, readiness.CanCollectAll);
        foreach (var source in readiness.Sources)
        {
            var row = Assert.Single(daily.Stalls, x => x.StallId == source.StallId);
            Assert.Equal(row.IsCollectableToday && !row.IsCollectedToday && !row.IsAbsentToday, source.CanCollect);
            Assert.Equal(row.DailyRate, source.EffectiveCharge);
        }
        if (paid < 5)
        {
            var intent = new NpmDailyBatchIntent(Guid.NewGuid(), Today, readiness.Sources.Where(x => x.CanCollect)
                .Select(x => new NpmDailyBatchItem(Guid.NewGuid(), x.StallId)).ToArray());
            var quoteResponse = await host.Client.PostAsJsonAsync("/api/mobile/npm-daily-batch/quote", intent, CollectorApiHost.Json);
            quoteResponse.EnsureSuccessStatusCode();
            var quote = (await quoteResponse.Content.ReadFromJsonAsync<NpmDailyBatchQuote>(CollectorApiHost.Json))!;
            Assert.True(quote.CanRecord);
            Assert.Equal(readiness.EligibleTotal, quote.Total);
            Assert.Equal(5 - paid, quote.ItemCount);
        }
        Assert.Equal(paid, await ctx.Collections.CountAsync()); // Reads/quotes never post the paid stalls again.
    }

    [SkippableFact]
    public async Task Legacy_daily_is_not_a_canonical_SRC_batch_and_readiness_states_the_boundary()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync(); var w = await SeedAsync(false);
        await using var ctx = db.CreateContext(w.TenantId);
        await using var host = await CollectorApiHost.StartAsync(db, w.CollectorId, w.TenantId);
        var readiness = (await host.Client.GetFromJsonAsync<NpmDailyBatchReadiness>("/api/mobile/npm-daily-batch/readiness", CollectorApiHost.Json))!;
        Assert.False(readiness.CanCollectAll);
        Assert.All(readiness.Sources, x => Assert.Equal("SourceStillLegacy", x.ReasonCode));
        var ordinary = await host.Client.PostAsJsonAsync("/api/mobile/npm/collections/record",
            new RecordMobileNpmCollectionRequest(w.StallA, true, CollectionDate: Today), CollectorApiHost.Json);
        Assert.True(ordinary.IsSuccessStatusCode, await ordinary.Content.ReadAsStringAsync());
        Assert.True((await ctx.DailyCollections.SingleAsync()).IsPaid);
        Assert.Empty(await ctx.Collections.ToListAsync()); // Legacy payment is not a canonical Collection/SRC.
        var after = (await host.Client.GetFromJsonAsync<NpmDailyBatchReadiness>("/api/mobile/npm-daily-batch/readiness", CollectorApiHost.Json))!;
        Assert.Equal("AlreadyCollected", Assert.Single(after.Sources, x => x.StallId == w.StallA).ReasonCode);
        var intent = new NpmDailyBatchIntent(Guid.NewGuid(), Today, [new(Guid.NewGuid(), w.StallB)]);
        var quote = (await Batch(ctx, w).PreviewAsync(intent)).Value!;
        Assert.False(quote.CanRecord);
        Assert.Contains(quote.Problems, x => x.Code == "SourceStillLegacy");
    }
}
