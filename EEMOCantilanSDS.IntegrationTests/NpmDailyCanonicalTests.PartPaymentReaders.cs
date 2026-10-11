using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Reader reconciliation for NPM part payments. The money is owned by the canonical Collections; every report that reads NPM day rows must
/// state the SAME cash as those Collections (neither missing the part received nor counting it twice), say Partial where it shows a status,
/// and leave exactly the remainder outstanding, before and after a correction.
/// </summary>
public sealed partial class NpmDailyCanonicalTests
{
    private async Task<Result<NpmDailyCanonicalPoster.Outcome>> PayThisMonthAsync(World w, decimal amount, Guid? operationId = null)
    {
        var quote = await PartQuoteAsync(w, amount, year: Today.Year, month: Today.Month);
        Assert.Equal("Applied", quote.Outcome);
        return await PartPostAsync(w, quote, operationId);
    }

    private sealed record Position(decimal Official, decimal FacilityRevenue, decimal LedgerCollected, decimal HistoryPaid, string HistoryStatus,
        decimal CollectorLines, decimal FeedAmount, string FeedStatus, decimal DayRowSettled, decimal CanonicalNet);

    private async Task<Position> PositionAsync(World w)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        var official = await IncomeAsync(w, "RENT_NPM");
        var report = await new FacilityReportsRepository(ctx).GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, Today.Year, Today.Month, null, default);
        var ledger = await new PaymentRepository(ctx).GetStallLedgerSummaryAsync(w.StallA, default);
        var history = (await new PaymentRepository(ctx).GetPaymentHistoryAsync(w.StallA, default))
            .SingleOrDefault(h => h.Period == Today.ToString("yyyy-MM"));
        var collector = await new CollectorReportQueries(ctx).GetCollectionsAsync(w.CollectorId, Today, Today);
        var feed = (await new TransactionFeedRepository(ctx).GetRecentTransactionsAsync(FacilityCode.NPM, Today, 50)).Where(f => f.Reference.Contains("Stall V-01")).ToList();
        var day = await ctx.DailyCollections.AsNoTracking().Where(d => d.StallId == w.StallA).SumAsync(d => d.IsPaid ? d.DailyFee : d.CanonicalSettledAmount);
        var voided = await ctx.CollectionCorrections.AsNoTracking().Select(c => c.OriginalCollectionId).ToListAsync();
        var net = await ctx.Collections.AsNoTracking().Where(c => !voided.Contains(c.Id)).SumAsync(c => c.TotalAmount);
        return new(official, report.TotalRevenue, ledger.TotalCollected, history?.AmountPaid ?? 0m, history?.Status.ToString() ?? "-",
            collector.Lines.Sum(l => l.Amount), feed.Sum(f => f.Amount), feed.FirstOrDefault()?.Status ?? "-", day, net);
    }

    [SkippableFact]
    public async Task PartPayment_EveryReaderStatesTheSameCash_PartialIsShown_AndTheRemainderStaysOutstanding()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var first = (await PayThisMonthAsync(w, 20m)).Value!;

        var afterTwenty = await PositionAsync(w);
        // One truth: the canonical Collections' net cash (₱20) — never ₱30 (the whole fee) and never ₱40 (counted twice).
        Assert.Equal(20m, afterTwenty.CanonicalNet);
        Assert.Equal(20m, afterTwenty.Official);
        Assert.Equal(20m, afterTwenty.DayRowSettled);
        Assert.Equal(20m, afterTwenty.FacilityRevenue);
        Assert.Equal(20m, afterTwenty.LedgerCollected);
        Assert.Equal((20m, "Partial"), (afterTwenty.HistoryPaid, afterTwenty.HistoryStatus));
        Assert.Equal(20m, afterTwenty.CollectorLines);
        Assert.Equal((20m, "Partial"), (afterTwenty.FeedAmount, afterTwenty.FeedStatus));
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var arrears = await new StallRepository(ctx).GetMobileNpmArrearsAsync(Today.Year, Today.Month, Today, default);
            var row = arrears.Payors.SingleOrDefault(p => p.StallId == w.StallA);
            Assert.Equal(10m, row?.PartPaidBalance ?? 0m);                        // the rest of the part-paid day is stated as owed
        }

        var second = (await PayThisMonthAsync(w, 10m)).Value!;
        var complete = await PositionAsync(w);
        Assert.NotEqual(first.CollectionId, second.CollectionId);
        Assert.All(new[] { complete.CanonicalNet, complete.Official, complete.DayRowSettled, complete.FacilityRevenue, complete.LedgerCollected, complete.CollectorLines }, v => Assert.Equal(30m, v));
        Assert.Equal(30m, complete.FeedAmount);                                  // both payments, once

        await using (var ctx = db.CreateContext(w.TenantId))                      // a correction of the first takes back exactly its ₱20
            Assert.True((await Poster(ctx, w.TenantId, Admin(w)).VoidAsync(first.CollectionId, "Wrong stall")).IsSuccess);
        var afterVoid = await PositionAsync(w);
        Assert.All(new[] { afterVoid.CanonicalNet, afterVoid.Official, afterVoid.DayRowSettled, afterVoid.FacilityRevenue, afterVoid.LedgerCollected, afterVoid.CollectorLines }, v => Assert.Equal(10m, v));
    }

    [SkippableFact]
    public async Task PartPayment_FortyFiveAcrossTwoDays_AllReadersAgree_AndWholeDayHistoryIsUntouched()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        // An ordinary whole-day payment first (existing path), then ₱45 by amount: the readers must add them exactly once each.
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Record(ctx, w.TenantId, Collector(w)).Handle(new(w.StallB, Today, true, null, null, Guid.NewGuid()), CancellationToken.None)).IsSuccess);
        Assert.True((await PayThisMonthAsync(w, 45m)).IsSuccess);

        await using var verify = db.CreateContext(w.TenantId);
        var voided = await verify.CollectionCorrections.AsNoTracking().Select(c => c.OriginalCollectionId).ToListAsync();
        var net = await verify.Collections.AsNoTracking().Where(c => !voided.Contains(c.Id)).SumAsync(c => c.TotalAmount);
        Assert.Equal(75m, net);                                                   // ₱30 whole day + ₱45
        Assert.Equal(75m, await IncomeAsync(w, "RENT_NPM"));
        var report = await new FacilityReportsRepository(verify).GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, Today.Year, Today.Month, null, default);
        Assert.Equal(75m, report.TotalRevenue);
        var rows = await verify.DailyCollections.AsNoTracking().Where(d => d.StallId == w.StallA).OrderBy(d => d.CollectionDate).ToListAsync();
        Assert.Equal([(true, 30m), (false, 15m)], rows.Select(r => (r.IsPaid, r.CanonicalSettledAmount)));
        Assert.Single(await verify.DailyCollections.AsNoTracking().Where(d => d.StallId == w.StallB && d.IsPaid && d.CanonicalSettledAmount == 0m).ToListAsync());   // the whole day keeps its original meaning
    }
}
