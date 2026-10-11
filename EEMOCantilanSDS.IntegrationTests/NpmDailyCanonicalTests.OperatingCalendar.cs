using EEMOCantilanSDS.Application.Queries.DailyCollections.GetDailyCollectionMonth;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The New Public Market's weekly operating schedule on PostgreSQL: one effective answer for every reader (the Collection
/// Manager calendar, settlement, the Mobile collector's arrears and the closure repository), prospective from its effective
/// date, with explicit openings and closures winning over it and money already collected never touched.
/// </summary>
public sealed partial class NpmDailyCanonicalTests
{
    private static int Weekends(int year, int month, DateOnly? from = null) =>
        Enumerable.Range(1, DateTime.DaysInMonth(year, month)).Select(d => new DateOnly(year, month, d))
            .Count(d => (from is null || d >= from) && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);

    private async Task AddScheduleAsync(World w, DateOnly effectiveFrom, params DayOfWeek[] closed)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        ctx.NpmOperatingSchedules.Add(NpmOperatingSchedule.Create(effectiveFrom, closed, "Head memorandum", "head"));
        await ctx.SaveChangesAsync();
    }

    private async Task<(int Closed, int Missed, bool AnyPaidClosed)> WebMonthAsync(World w, Guid stallId, int year, int month)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        var handler = new GetDailyCollectionMonthQueryHandler(new DailyCollectionRepository(ctx), new StallRepository(ctx),
            new FeeRateResolver(ctx), new NpmMarketClosureRepository(ctx), new Clock());
        var result = (await handler.Handle(new GetDailyCollectionMonthQuery(stallId, year, month), CancellationToken.None)).Value!;
        return (result.DaysClosed, result.DaysMissed, result.Collections.Values.Any(d => d.IsPaid && d.IsMarketClosed));
    }

    [SkippableFact]
    public async Task WeeklySchedule_ClosesWeekendsFromItsEffectiveDate_ForEveryReaderAtOnce_AndNeverRewritesEarlierMonths()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var past = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var earlier = past.AddMonths(-1);

        // Before any schedule is set, nothing is closed: today's figures do not move.
        Assert.Equal(0, (await WebMonthAsync(w, w.StallA, past.Year, past.Month)).Closed);
        Assert.Equal(900m, await PayableForMonthAsync(w, w.StallA, past.Year, past.Month));

        await AddScheduleAsync(w, past, DayOfWeek.Saturday, DayOfWeek.Sunday);

        var weekends = Weekends(past.Year, past.Month);
        // The Collection Manager calendar and the closure repository agree on exactly those days.
        var web = await WebMonthAsync(w, w.StallA, past.Year, past.Month);
        Assert.Equal(weekends, web.Closed);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var closures = await new NpmMarketClosureRepository(ctx).GetByMonthAsync(past.Year, past.Month);
            Assert.Equal(weekends, closures.Count);
            Assert.All(closures, c => Assert.True(c.ClosureDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
            Assert.Empty(await ctx.NpmMarketClosures.ToListAsync());      // a scheduled day stores no row
        }

        // Settlement reads the same answer. On the fixed-rent basis each closed day is credited at one installment (the
        // existing closure rule), so the month owes its rent less those days — never the whole ₱900 for days nobody traded.
        Assert.Equal(900m - 30m * weekends, await PayableForMonthAsync(w, w.StallA, past.Year, past.Month));

        // The month BEFORE the schedule is untouched: a past weekend is never re-read under a rule adopted later.
        Assert.Equal(0, (await WebMonthAsync(w, w.StallA, earlier.Year, earlier.Month)).Closed);
        Assert.Equal(900m, await PayableForMonthAsync(w, w.StallA, earlier.Year, earlier.Month));
    }

    [SkippableFact]
    public async Task OpeningAScheduledWeekend_MakesItBillableAgain_AndRestoringTheDefaultClosesItAgain()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var past = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        await AddScheduleAsync(w, past, DayOfWeek.Saturday, DayOfWeek.Sunday);
        var saturday = Enumerable.Range(1, DateTime.DaysInMonth(past.Year, past.Month)).Select(d => new DateOnly(past.Year, past.Month, d))
            .First(d => d.DayOfWeek == DayOfWeek.Saturday);
        var weekends = Weekends(past.Year, past.Month);

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            ctx.NpmMarketClosures.Add(NpmMarketClosure.CreateOpenOverride(saturday, "Working Saturday", "head"));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(weekends - 1, (await WebMonthAsync(w, w.StallA, past.Year, past.Month)).Closed);
        Assert.Equal(900m - 30m * (weekends - 1), await PayableForMonthAsync(w, w.StallA, past.Year, past.Month));

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            ctx.NpmMarketClosures.Remove(await ctx.NpmMarketClosures.SingleAsync());   // restore the default
            await ctx.SaveChangesAsync();
        }
        Assert.Equal(weekends, (await WebMonthAsync(w, w.StallA, past.Year, past.Month)).Closed);
    }

    [SkippableFact]
    public async Task ClosingAWeekday_ByAnExplicitClosure_StillWorksAlongsideTheSchedule()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var past = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        await AddScheduleAsync(w, past, DayOfWeek.Saturday, DayOfWeek.Sunday);
        var weekday = Enumerable.Range(1, DateTime.DaysInMonth(past.Year, past.Month)).Select(d => new DateOnly(past.Year, past.Month, d))
            .First(d => d.DayOfWeek == DayOfWeek.Wednesday);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            ctx.NpmMarketClosures.Add(NpmMarketClosure.Create(weekday, MarketClosureReason.Holiday, "Holiday", "head"));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(Weekends(past.Year, past.Month) + 1, (await WebMonthAsync(w, w.StallA, past.Year, past.Month)).Closed);
    }

    [SkippableFact]
    public async Task APaymentAlreadyCollectedOnADayThatIsLaterScheduledClosed_StaysPaid()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var past = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var saturday = Enumerable.Range(1, DateTime.DaysInMonth(past.Year, past.Month)).Select(d => new DateOnly(past.Year, past.Month, d))
            .First(d => d.DayOfWeek == DayOfWeek.Saturday);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var row = EEMOCantilanSDS.Domain.Entities.Payments.DailyCollection.Create(w.StallA, saturday, "office", 30m);
            row.MarkPaid("OR-LEGACY-1", w.CollectorId, null, "office");
            ctx.DailyCollections.Add(row);
            await ctx.SaveChangesAsync();
        }

        await AddScheduleAsync(w, past, DayOfWeek.Saturday, DayOfWeek.Sunday);

        await using var read = db.CreateContext(w.TenantId);
        var paid = await read.DailyCollections.AsNoTracking().SingleAsync(d => d.StallId == w.StallA && d.CollectionDate == saturday);
        Assert.True(paid.IsPaid);                                           // the posted receipt is untouched
        Assert.Equal(30m, paid.DailyFee);
        var web = await WebMonthAsync(w, w.StallA, past.Year, past.Month);
        Assert.False(web.AnyPaidClosed);                                    // paid on a closed day reads Paid, never Closed
    }

    [SkippableFact]
    public async Task TheMobileArrearsScreenAgreesWithSettlementOnAScheduledMonth()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: false);
        var past = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        await AddScheduleAsync(w, past, DayOfWeek.Saturday, DayOfWeek.Sunday);

        await using var ctx = db.CreateContext(w.TenantId);
        var arrears = await new StallRepository(ctx).GetMobileNpmArrearsAsync(Today.Year, Today.Month, Today, CancellationToken.None);

        var stall = arrears.Payors.Single(p => p.StallId == w.StallA);
        var pastMonth = stall.PastMonths.Single(m => m.Year == past.Year && m.Month == past.Month);
        Assert.Equal(await PayableForMonthAsync(w, w.StallA, past.Year, past.Month), pastMonth.Amount);
        Assert.Equal(900m - 30m * Weekends(past.Year, past.Month), pastMonth.Amount);
    }

    private async Task<decimal> PayableForMonthAsync(World w, Guid stallId, int year, int month)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        var stall = (await new StallRepository(ctx).GetByIdAsync(stallId, CancellationToken.None))!;
        return (await Settlement(ctx).ComputePayableAsync(stall, year, month, CancellationToken.None)).Amount;
    }
}
