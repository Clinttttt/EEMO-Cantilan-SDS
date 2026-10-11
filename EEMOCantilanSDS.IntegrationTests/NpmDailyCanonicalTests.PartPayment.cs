using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// NPM part payments on PostgreSQL: the actual amount received is applied oldest-first to the installments payable today, as full or part
/// installments. Each payment is its own Collection and SRC; none is rewritten; a correction takes back only its own money; more than is
/// owed is refused and never kept as an advance, a future day or an unapplied credit.
/// </summary>
public sealed partial class NpmDailyCanonicalTests
{
    private static NpmPartPaymentWorkflow Part(AppDbContext ctx, World w, Actor actor, bool enabled = true) => new(
        new StallRepository(ctx), new CollectorRepository(ctx), new DailyCollectionRepository(ctx), Settlement(ctx),
        Poster(ctx, w.TenantId, actor), actor, new Clock(), new NoCache(), new FixedCode(), new NpmPartPaymentOptions(enabled));

    private static DateOnly PastMonth => new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);

    private async Task<NpmPartPaymentQuoteDto> PartQuoteAsync(World w, decimal amount, Guid? stall = null, int? year = null, int? month = null, DateOnly? on = null, Actor? actor = null)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        var quote = await Part(ctx, w, actor ?? Collector(w)).QuoteAsync(stall ?? w.StallA, year ?? PastMonth.Year, month ?? PastMonth.Month, amount, collectionDate: on);
        Assert.True(quote.IsSuccess, quote.Error);
        return quote.Value!;
    }

    private async Task<Result<NpmDailyCanonicalPoster.Outcome>> PartPostAsync(World w, NpmPartPaymentQuoteDto quote, Guid? operationId = null, decimal? amount = null, Actor? actor = null)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        return await Part(ctx, w, actor ?? Collector(w)).PostAsync(new NpmPartPaymentRequest(
            operationId ?? Guid.NewGuid(), quote.StallId, quote.Year, quote.Month, quote.BusinessDate, amount ?? quote.Amount, quote.QuoteToken));
    }

    private async Task<Result<NpmDailyCanonicalPoster.Outcome>> PayAsync(World w, decimal amount, Guid? stall = null, Guid? operationId = null)
    {
        var quote = await PartQuoteAsync(w, amount, stall);
        Assert.Equal("Applied", quote.Outcome);
        return await PartPostAsync(w, quote, operationId);
    }

    private async Task<DailyCollection?> DayAsync(World w, DateOnly day, Guid? stall = null)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        return await ctx.DailyCollections.AsNoTracking().SingleOrDefaultAsync(d => d.StallId == (stall ?? w.StallA) && d.CollectionDate == day);
    }

    private async Task<int> CollectionCountAsync(World w)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        return await ctx.Collections.CountAsync();
    }

    [SkippableFact]
    public async Task PartPayment_TwentyAgainstThirty_IsPartial_OneCollection_AndTenPesosStayOwed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var first = PastMonth;
        var owedBefore = (await PartQuoteAsync(w, 30m)).Payable;

        var quote = await PartQuoteAsync(w, 20m);
        Assert.Equal("Applied", quote.Outcome);
        var line = Assert.Single(quote.Lines);
        Assert.Equal((first, 30m, 0m, 20m, 10m, "Partial"), (line.Day, line.Fee, line.Settled, line.Applied, line.Remaining, line.Status));
        Assert.Equal(owedBefore - 20m, quote.RemainingAfter);
        Assert.Equal(0, await CollectionCountAsync(w));                       // quoting records nothing

        var posted = await PartPostAsync(w, quote);
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(20m, posted.Value!.Amount);

        var day = (await DayAsync(w, first))!;
        Assert.Equal((false, 20m, 10m, SettlementAuthority.Canonical), (day.IsPaid, day.CanonicalSettledAmount, day.RemainingBaseFee, day.SettlementAuthorityState));
        Assert.True(day.IsPartlyPaid);
        Assert.Equal(posted.Value.CollectionId, day.CanonicalCollectionId);
        await using var verify = db.CreateContext(w.TenantId);
        var collection = await verify.Collections.Include(c => c.Lines).ThenInclude(l => l.Allocations).SingleAsync();
        Assert.Equal(20m, collection.TotalAmount);                            // the actual money received, not the ₱30 fee
        var allocation = Assert.Single(Assert.Single(collection.Lines).Allocations);
        Assert.Equal((day.Id, 20m), (allocation.SourceId, allocation.Amount));
        Assert.Equal(20m, await IncomeAsync(w, "RENT_NPM"));                  // revenue recognises ₱20, not ₱30
    }

    [SkippableFact]
    public async Task PartPayment_ASecondTenPesos_CompletesTheDay_AndBothCollectionsRemainTraceable()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var first = PastMonth;
        var one = (await PayAsync(w, 20m)).Value!;

        var quote = await PartQuoteAsync(w, 10m);
        var line = Assert.Single(quote.Lines);
        Assert.Equal((first, 20m, 10m, 0m, "Paid"), (line.Day, line.Settled, line.Applied, line.Remaining, line.Status));
        var two = (await PartPostAsync(w, quote)).Value!;

        var day = (await DayAsync(w, first))!;
        Assert.Equal((true, 30m, 0m, two.CollectionId), (day.IsPaid, day.CanonicalSettledAmount, day.RemainingBaseFee, day.CanonicalCollectionId));
        Assert.NotEqual(one.CollectionId, two.CollectionId);
        Assert.NotEqual(one.ReferenceCode, two.ReferenceCode);
        await using var verify = db.CreateContext(w.TenantId);
        var collections = await verify.Collections.Include(c => c.Lines).ThenInclude(l => l.Allocations).OrderBy(c => c.ReferenceNumber).ToListAsync();
        Assert.Equal([20m, 10m], collections.Select(c => c.TotalAmount));      // the first ₱20 is not rewritten as ₱30
        Assert.All(collections, c => Assert.Equal(day.Id, Assert.Single(Assert.Single(c.Lines).Allocations).SourceId));
        Assert.Equal(30m, await IncomeAsync(w, "RENT_NPM"));
        Assert.Equal(first.AddDays(1), (await PartQuoteAsync(w, 30m)).Lines.Single().Day);   // the next installment is next
    }

    [SkippableFact]
    public async Task PartPayment_FortyFiveAgainstTwoDays_PaysTheOldestAndPartsTheNext_InOneCollection()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var first = PastMonth;

        var quote = await PartQuoteAsync(w, 45m);

        Assert.Equal(2, quote.Lines.Count);
        Assert.Equal(("Paid", 30m, first), (quote.Lines[0].Status, quote.Lines[0].Applied, quote.Lines[0].Day));
        Assert.Equal(("Partial", 15m, 15m, first.AddDays(1)), (quote.Lines[1].Status, quote.Lines[1].Applied, quote.Lines[1].Remaining, quote.Lines[1].Day));
        var posted = await PartPostAsync(w, quote);
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(1, await CollectionCountAsync(w));
        await using var verify = db.CreateContext(w.TenantId);
        var allocations = (await verify.Collections.Include(c => c.Lines).ThenInclude(l => l.Allocations).SingleAsync()).Lines.Single().Allocations;
        Assert.Equal([30m, 15m], allocations.OrderByDescending(a => a.Amount).Select(a => a.Amount));
        Assert.Equal(45m, await IncomeAsync(w, "RENT_NPM"));
        Assert.Equal((true, false), ((await DayAsync(w, first))!.IsPaid, (await DayAsync(w, first.AddDays(1)))!.IsPaid));
    }

    [SkippableFact]
    public async Task PartPayment_MoreThanIsOwed_IsRefused_NeverAnAdvanceAFutureDayOrACredit()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var payable = (await PartQuoteAsync(w, 30m)).Payable;

        var over = await PartQuoteAsync(w, payable + 40m);

        Assert.Equal("ExceedsPayable", over.Outcome);
        Assert.Empty(over.Lines);
        Assert.Equal(payable, over.Payable);
        var refused = await PartPostAsync(w, over);
        Assert.False(refused.IsSuccess);
        Assert.Equal(0, await CollectionCountAsync(w));
        await using var verify = db.CreateContext(w.TenantId);
        Assert.Equal(0, await verify.DailyCollections.CountAsync());          // no day was marked, none created

        // The current month: only the days that have happened can be paid; the rest of the month is not prepaid here.
        var current = await PartQuoteAsync(w, 5_000m, year: Today.Year, month: Today.Month);
        Assert.Equal("ExceedsPayable", current.Outcome);
        Assert.Equal(Today.Day * 30m, current.Payable);
    }

    [SkippableTheory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10.005")]
    public async Task PartPayment_ZeroNegativeOrExcessPrecision_IsInvalid(string amount)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);

        var quote = await PartQuoteAsync(w, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal("Invalid", quote.Outcome);
        Assert.False((await PartPostAsync(w, quote with { Amount = 10m })).IsSuccess);
        Assert.Equal(0, await CollectionCountAsync(w));
    }

    [SkippableFact]
    public async Task PartPayment_SameOperationReplaysTheSameCollection_AChangedPayloadIsAConflict_AndAStaleMonthNeedsReconciliation()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var quote = await PartQuoteAsync(w, 20m);
        var operation = Guid.NewGuid();

        var first = await PartPostAsync(w, quote, operation);
        var replay = await PartPostAsync(w, quote, operation);

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal((first.Value!.CollectionId, first.Value.ReferenceCode, true), (replay.Value!.CollectionId, replay.Value.ReferenceCode, replay.Value.Existing));
        Assert.Equal(ResultStatus.Conflict, (await PartPostAsync(w, quote, operation, amount: 25m)).Status);
        Assert.Equal(1, await CollectionCountAsync(w));
        Assert.Equal(20m, await IncomeAsync(w, "RENT_NPM"));

        // The month moved after review (the first payment landed): the old reviewed quote must not be applied to the new month.
        var stale = await PartPostAsync(w, quote);
        Assert.StartsWith("RECONCILIATION_REQUIRED:", stale.Error);
        Assert.Equal(1, await CollectionCountAsync(w));
    }

    [SkippableFact]
    public async Task PartPayment_CorrectingTheFirstPartPayment_TakesBackOnlyItsOwnMoney()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var first = PastMonth;
        var one = (await PayAsync(w, 20m)).Value!;
        var two = (await PayAsync(w, 10m)).Value!;
        Assert.True((await DayAsync(w, first))!.IsPaid);

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var voided = await Poster(ctx, w.TenantId, Admin(w)).VoidAsync(one.CollectionId, "Wrong stall");
            Assert.True(voided.IsSuccess, voided.Error);
            Assert.False((await Poster(ctx, w.TenantId, Admin(w)).VoidAsync(one.CollectionId, "again")).IsSuccess);   // corrected once
        }

        var day = (await DayAsync(w, first))!;
        Assert.Equal((false, 10m, 20m, two.CollectionId), (day.IsPaid, day.CanonicalSettledAmount, day.RemainingBaseFee, day.CanonicalCollectionId));
        Assert.Equal(10m, await IncomeAsync(w, "RENT_NPM"));                  // the second payment stays in force
        await using (var verify = db.CreateContext(w.TenantId))
        {
            Assert.Equal(2, await verify.Collections.CountAsync());           // append-only: nothing deleted or rewritten
            var correction = await verify.CollectionCorrections.SingleAsync();
            Assert.Equal((CollectionCorrectionType.Void, -20m, one.CollectionId), (correction.CorrectionType, correction.FinancialEffectAmount, correction.OriginalCollectionId));
        }
        var again = await PartQuoteAsync(w, 20m);
        Assert.Equal((20m, 10m, first), (again.Lines.Single().Applied, again.Lines.Single().Settled, again.Lines.Single().Day));   // ₱20 is owed again

        await using (var ctx = db.CreateContext(w.TenantId))                  // and the rest can be voided too: the day is unsettled again
        {
            Assert.True((await Poster(ctx, w.TenantId, Admin(w)).VoidAsync(two.CollectionId, "Wrong stall")).IsSuccess);
        }
        var cleared = (await DayAsync(w, first))!;
        Assert.Equal((false, 0m, (Guid?)null), (cleared.IsPaid, cleared.CanonicalSettledAmount, cleared.CanonicalCollectionId));
        Assert.Equal(0m, await IncomeAsync(w, "RENT_NPM"));
    }

    [SkippableFact]
    public async Task PartPayment_APartPaidDayIsNeverTakenWhole_ByTheWholeDayPaths()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var first = PastMonth;
        Assert.True((await PayAsync(w, 20m)).IsSuccess);

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var settle = await SettleDays(ctx, w.TenantId, Collector(w)).Handle(new SettleNpmDaysCommand(w.StallA, [first], null), CancellationToken.None);
            Assert.False(settle.IsSuccess);                                  // skipped: it is completed by paying the remainder
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var record = await Record(ctx, w.TenantId, Collector(w)).Handle(
                new RecordDailyCollectionCommand(w.StallA, first, true, null, null, Guid.NewGuid()), CancellationToken.None);
            Assert.Equal(ResultStatus.Conflict, record.Status);
            var excuse = await Record(ctx, w.TenantId, Admin(w)).Handle(new RecordDailyCollectionCommand(w.StallA, first, false, IsAbsent: true), CancellationToken.None);
            Assert.False(excuse.IsSuccess);
        }
        Assert.Equal(1, await CollectionCountAsync(w));
        Assert.Equal(20m, (await DayAsync(w, first))!.CanonicalSettledAmount);
        Assert.Equal(20m, await IncomeAsync(w, "RENT_NPM"));
    }

    [SkippableFact]
    public async Task PartPayment_ExcusedAndMissingDaysAreNotCharged_AndTheMonthCeilingHolds()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var first = PastMonth;
        await using (var ctx = db.CreateContext(w.TenantId))                  // the first day is excused: the payor owes nothing for it
        {
            var excused = DailyCollection.Create(w.StallA, first, "test");
            excused.MarkAbsent("test");
            ctx.DailyCollections.Add(excused);
            await ctx.SaveChangesAsync();
        }

        var quote = await PartQuoteAsync(w, 30m);

        Assert.Equal(first.AddDays(1), quote.Lines.Single().Day);             // the excused day is skipped
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);
        Assert.True(quote.Payable <= 900m - 30m);                             // never past the month's own rent (less the day nothing is owed for)
        Assert.True(quote.Payable <= (daysInMonth - 1) * 30m);
    }

    [SkippableFact]
    public async Task PartPayment_NeedsCanonicalAuthority_AndAnAssignedCollector_AndTheStallsOwnTenant()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var legacy = await SeedAsync(canonical: false);
        await using (var ctx = db.CreateContext(legacy.TenantId))
        {
            var quote = await Part(ctx, legacy, Collector(legacy)).QuoteAsync(legacy.StallA, PastMonth.Year, PastMonth.Month, 20m);
            Assert.Equal(ResultStatus.Conflict, quote.Status);                // a legacy row carries one payment event only
        }

        var w = await SeedAsync(canonical: true, assign: false);
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.Equal(ResultStatus.Forbidden, (await Part(ctx, w, Collector(w)).QuoteAsync(w.StallA, PastMonth.Year, PastMonth.Month, 20m)).Status);

        var other = await SeedAsync(canonical: true);
        await using (var ctx = db.CreateContext(other.TenantId))              // another tenant's stall is not found, never quoted
            Assert.Equal(ResultStatus.NotFound, (await Part(ctx, other, Collector(other)).QuoteAsync(w.StallA, PastMonth.Year, PastMonth.Month, 20m)).Status);

        var yesterday = Today.AddDays(-1);                                    // before the switch the date is not canonical money
        await using (var ctx = db.CreateContext(other.TenantId))
            Assert.Equal(ResultStatus.Conflict, (await Part(ctx, other, Collector(other)).QuoteAsync(other.StallA, PastMonth.Year, PastMonth.Month, 20m, collectionDate: yesterday)).Status);
    }

    [SkippableFact]
    public async Task PartPayment_ShortMonth_TakesTheInstallmentsInParts_AndTheMonthEndAdjustmentOnlyWhole()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var year = Today.Month > 2 ? Today.Year : Today.Year - 1;
        var w = await SeedAsync(canonical: true, lettingStart: new DateOnly(year, 1, 1));
        var days = DateTime.DaysInMonth(year, 2);

        var quote = await PartQuoteAsync(w, 30m, year: year, month: 2);
        Assert.Equal((days * 30m, 900m - days * 30m), (quote.Payable, quote.Adjustment));   // 28 installments, and the ₱60 that is not an installment
        var over = await PartQuoteAsync(w, days * 30m + 5m, year: year, month: 2);
        Assert.Equal("ExceedsPayable", over.Outcome);                          // the adjustment is not collectable in parts

        var all = await PartQuoteAsync(w, days * 30m, year: year, month: 2);
        Assert.Equal(days, all.Lines.Count);
        Assert.True((await PartPostAsync(w, all)).IsSuccess);
        Assert.Equal(days * 30m, await IncomeAsync(w, "RENT_NPM"));

        // What remains is the month-end adjustment, collected by the existing whole-payment path on the last paid installment.
        await using var ctx = db.CreateContext(w.TenantId);
        var whole = Whole(ctx, w, Collector(w));
        var wholeQuote = (await whole.QuoteAsync(w.StallA, year, 2)).Value!;
        Assert.Equal(900m - days * 30m, wholeQuote.Amount);
        Assert.True((await whole.PostAsync(WholeRequest(w.StallA, wholeQuote))).IsSuccess);
        Assert.Equal(2, await ctx.Collections.CountAsync());
        Assert.Equal(0m, (await whole.QuoteAsync(w.StallA, year, 2)).Value!.Amount);
    }

    [SkippableFact]
    public async Task PartPayment_ThirtyOneDayMonth_NeverTakesMoreThanTheMonthlyRent()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var past = PastMonth;

        var quote = await PartQuoteAsync(w, 30m);

        Assert.Equal(900m, quote.Payable);                                    // 30 or 31 days alike: the rent, not days x 30
        Assert.Equal("ExceedsPayable", (await PartQuoteAsync(w, 905m)).Outcome);
        var full = await PartQuoteAsync(w, 900m);
        Assert.True((await PartPostAsync(w, full)).IsSuccess);
        Assert.Equal(0m, (await PartQuoteAsync(w, 30m)).Payable);
        Assert.Equal("NothingPayable", (await PartQuoteAsync(w, 30m)).Outcome);
        Assert.Equal(900m, await IncomeAsync(w, "RENT_NPM"));
        Assert.Equal(past.Month, (await DayAsync(w, past))!.CollectionDate.Month);
    }

    [SkippableFact]
    public async Task PartPayment_TwoCollectorsPayingTheLastTenPesos_OnlyOneIsApplied()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        var first = PastMonth;
        Assert.True((await PayAsync(w, 20m)).IsSuccess);
        var quote = await PartQuoteAsync(w, 10m);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { return await PartPostAsync(w, quote); }
            catch (Exception ex) { return Result<NpmDailyCanonicalPoster.Outcome>.Failure("UNHANDLED " + ex.GetType().Name, ResultStatus.Invalid); }
        }));

        Assert.DoesNotContain(results, r => (r.Error ?? "").StartsWith("UNHANDLED"));       // the loser is a clean conflict, never an exception
        Assert.Equal(1, results.Count(r => r.IsSuccess));  
        Assert.Contains(results, r => r.Status == ResultStatus.Conflict);
        Assert.Equal(1, results.Count(r => r.IsSuccess));                     // never both: the day cannot be settled twice
        var day = (await DayAsync(w, first))!;
        Assert.Equal((true, 30m), (day.IsPaid, day.CanonicalSettledAmount));
        Assert.Equal(2, await CollectionCountAsync(w));
        Assert.Equal(30m, await IncomeAsync(w, "RENT_NPM"));
    }

    [SkippableFact]
    public async Task PartPayment_IsOffUnlessTheOfficeSwitchesItOn_AndTheRegisterShowsAPartPaidDayAsPartial()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(canonical: true);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var off = await Part(ctx, w, Collector(w), enabled: false).QuoteAsync(w.StallA, PastMonth.Year, PastMonth.Month, 20m);
            Assert.Equal(ResultStatus.Conflict, off.Status);
            Assert.Equal(0, await ctx.Collections.CountAsync());
        }

        Assert.True((await PayAsync(w, 20m)).IsSuccess);
        await using var verify = db.CreateContext(w.TenantId);
        var month = (await new EEMOCantilanSDS.Application.Queries.DailyCollections.GetDailyCollectionMonth.GetDailyCollectionMonthQueryHandler(
            new DailyCollectionRepository(verify), new StallRepository(verify), new EEMOCantilanSDS.Infrastructure.Fees.FeeRateResolver(verify), new NpmMarketClosureRepository(verify), new Clock())
            .Handle(new(w.StallA, PastMonth.Year, PastMonth.Month), CancellationToken.None)).Value!;
        var day = month.Collections[PastMonth.ToString("yyyy-MM-dd")];
        Assert.Equal((false, true, 20m, 0m), (day.IsPaid, day.IsPartial, day.PartSettled, day.AmountCollected));
        Assert.Equal(20m, month.TotalDailyFee);                       // the money received is in the month, though the day is not complete
        Assert.Equal(0, month.DaysCollected);                          // and the day is still not counted as paid
    }
}
