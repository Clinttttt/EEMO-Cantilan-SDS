using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Queries.DailyCollections.QuoteNpmAmount;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Recording an NPM payment by amount on PostgreSQL: only an amount that is exactly a sum of whole payable days is recorded, oldest
/// first and through the existing day settlement; anything else is previewed and never becomes a paid day, a partial day or an advance.
/// </summary>
public sealed partial class NpmDailyCanonicalTests
{
    private async Task<Application.Dtos.DailyCollections.NpmAmountQuoteDto> QuoteAsync(World w, int year, int month, decimal amount)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        var result = await new QuoteNpmAmountQueryHandler(new StallRepository(ctx), Settlement(ctx))
            .Handle(new QuoteNpmAmountQuery(w.StallA, year, month, amount), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [SkippableFact]
    public async Task AnAmountThatIsWholeInstallments_SettlesThoseOldestDays_AndNothingElse()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var past = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);

        var quote = await QuoteAsync(w, past.Year, past.Month, 90m);

        Assert.Equal("Exact", quote.Outcome);
        Assert.Equal([past, past.AddDays(1), past.AddDays(2)], quote.ExactDates);   // oldest first
        Assert.Equal(0m, quote.Unallocated);

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var settled = await SettleDays(ctx, w.TenantId, Collector(w)).Handle(new SettleNpmDaysCommand(w.StallA, quote.ExactDates, null), CancellationToken.None);
            Assert.True(settled.IsSuccess, settled.Error);
        }

        // Exactly three paid days were recorded, and the next quote starts after them.
        await using (var verify = db.CreateContext(w.TenantId))
            Assert.Equal(3, await verify.DailyCollections.CountAsync(d => d.StallId == w.StallA && d.IsPaid && d.CollectionDate.Month == past.Month));
        Assert.Equal(past.AddDays(3), (await QuoteAsync(w, past.Year, past.Month, 30m)).ExactDates.Single());

        // Replaying the same settlement does not record the days again.
        await using var replay = db.CreateContext(w.TenantId);
        var again = await SettleDays(replay, w.TenantId, Collector(w)).Handle(new SettleNpmDaysCommand(w.StallA, quote.ExactDates, null), CancellationToken.None);
        Assert.False(again.IsSuccess);
        await using var after = db.CreateContext(w.TenantId);
        Assert.Equal(3, await after.DailyCollections.CountAsync(d => d.StallId == w.StallA && d.IsPaid && d.CollectionDate.Month == past.Month));
    }

    [SkippableFact]
    public async Task AnIrregularAmount_IsNeverMadeIntoAPaidDay_ItIsPreviewedWithTheNearestValidAmounts()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var past = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);

        var quote = await QuoteAsync(w, past.Year, past.Month, 100m);

        Assert.Equal("NotWholeInstallments", quote.Outcome);
        Assert.Empty(quote.ExactDates);
        Assert.Equal(90m, quote.NearestBelow);
        Assert.Equal(120m, quote.NearestAbove);
        Assert.Equal(10m, quote.Unallocated);                       // stated, never recorded
        await using var verify = db.CreateContext(w.TenantId);
        Assert.Equal(0, await verify.DailyCollections.CountAsync(d => d.StallId == w.StallA));
    }

    [SkippableFact]
    public async Task MoreThanThePayableTotal_IsNotAnAdvance_AndThePreviewSaysWhatTheMonthOwes()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var past = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);

        var quote = await QuoteAsync(w, past.Year, past.Month, 5_000m);

        Assert.Equal("ExceedsPayable", quote.Outcome);
        Assert.Empty(quote.ExactDates);
        Assert.Equal(await PayableForMonthAsync(w, w.StallA, past.Year, past.Month), quote.PayableTotal);   // the month's own ceiling
        Assert.Equal(5_000m - quote.PayableTotal, quote.Unallocated);
    }

    [SkippableFact]
    public async Task ClosedWeekendsAreNotPayable_SoTheAmountNeverSettlesThem()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var past = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        await AddScheduleAsync(w, past, DayOfWeek.Saturday, DayOfWeek.Sunday);

        var quote = await QuoteAsync(w, past.Year, past.Month, 30m * 10);

        Assert.Equal("Exact", quote.Outcome);
        Assert.Equal(10, quote.ExactDates.Count);
        Assert.All(quote.ExactDates, d => Assert.False(d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
        Assert.Equal(900m - 30m * Weekends(past.Year, past.Month), (await QuoteAsync(w, past.Year, past.Month, 30m)).PayableTotal);
    }

    [SkippableFact]
    public async Task AFutureOrNothingPayableMonth_RecordsNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var next = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1);

        var quote = await QuoteAsync(w, next.Year, next.Month, 60m);

        Assert.Equal("NothingPayable", quote.Outcome);
        Assert.Empty(quote.ExactDates);
    }
}
