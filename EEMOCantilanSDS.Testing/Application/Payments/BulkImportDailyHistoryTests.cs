using EEMOCantilanSDS.Application.Command.Payments.BulkImportDailyHistory;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Payments;

/// <summary>
/// Recording the market's existing collection history.
///
/// <para>
/// The market is not billed by the month, so its history cannot be a monthly payment: it is a run of market days at a
/// fixed daily fee. The office's books record a COUNT of days against a receipt, not a rent figure, and this import
/// turns that count into the days it refers to.
/// </para>
///
/// <para>These tests hold the things that would otherwise let a count of days quietly become the wrong money: days
/// that have not happened, days nobody owes for, days already collected, and the same days claimed twice.</para>
/// </summary>
public class BulkImportDailyHistoryTests
{
    private static readonly Guid StallId = Guid.NewGuid();

    /// <summary>A month safely in the past, so "not started yet" never interferes with a test about days.</summary>
    private static readonly DateOnly Past = PhilippineTime.Today.AddMonths(-6);

    private static ImportDailyPaymentRow Row(
        int n, string stallNo, int year, int month, int days, string? or = "OR-1001") =>
        new(n, stallNo, "Kim Chui", year, month, days, or);

    /// <summary>A market space let from two years ago, so every month under test falls inside its term.</summary>
    private static Stall DailyStall(string stallNo = "1")
    {
        var stall = Stall.Create(Guid.NewGuid(), stallNo, 0m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Id))!.SetValue(stall, StallId);

        var contract = Contract.Create(
            stall.Id, "Kim Chui", "Kim Chui",
            PhilippineTime.Today.AddYears(-2), durationYears: 3, monthlyRate: 900m);
        stall.Contracts.Add(contract);
        return stall;
    }

    private static (BulkImportDailyHistoryCommandHandler Handler, List<DailyCollection> Added) Build(
        Stall? stall = null,
        IEnumerable<DailyCollection>? onRecord = null,
        IEnumerable<DateOnly>? closures = null,
        Facility? facility = null)
    {
        var theStall = stall ?? DailyStall();

        var stalls = new Mock<IStallRepository>();
        stalls.Setup(s => s.GetStallsWithContractsByFacilityAsync(
                It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { theStall });

        var added = new List<DailyCollection>();
        var existing = (onRecord ?? Array.Empty<DailyCollection>()).ToList();

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(d => d.GetByStallAndMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int y, int m, CancellationToken _) =>
                existing.Where(e => e.CollectionDate.Year == y && e.CollectionDate.Month == m).ToList());
        daily.Setup(d => d.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => { added.Add(dc); existing.Add(dc); })
            .Returns(Task.CompletedTask);

        var closureList = (closures ?? Array.Empty<DateOnly>()).ToList();
        var closureRepo = new Mock<INpmMarketClosureRepository>();
        closureRepo.Setup(c => c.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int y, int m, CancellationToken _) =>
                closureList.Where(d => d.Year == y && d.Month == m)
                           .Select(d => NpmMarketClosure.Create(d))
                           .ToList());

        var facilities = new Mock<IFacilityRepository>();
        facilities.Setup(f => f.GetByCodeAsync(It.IsAny<FacilityCode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(facility ?? Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        return (new BulkImportDailyHistoryCommandHandler(
            stalls.Object, daily.Object, Mock.Of<IPaymentRepository>(), closureRepo.Object,
            facilities.Object, CacheTestDoubles.FeeRateResolver, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant), added);
    }

    private static BulkImportDailyHistoryCommand Command(params ImportDailyPaymentRow[] rows) =>
        new(FacilityCode.NPM, MarketSection.VegetableArea, rows);

    [Fact]
    public async Task ACountOfDaysBecomesThatManyCollectedDays()
    {
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 12)), CancellationToken.None);

        Assert.Equal(12, added.Count);
        Assert.All(added, dc => Assert.True(dc.IsPaid));
        Assert.Equal(12, result.Value!.TotalDaysSettled);
        Assert.Equal(ImportDailyOutcome.RecordedInFull, result.Value!.Results[0].Outcome);

        // Earliest first, and each day distinct - a count of days that settled the same day twelve times would report
        // success while collecting one day's fee.
        Assert.Equal(added.Select(a => a.CollectionDate).Distinct().Count(), added.Count);
        Assert.Equal(added.OrderBy(a => a.CollectionDate).Select(a => a.CollectionDate), added.Select(a => a.CollectionDate));
    }

    [Fact]
    public async Task TheAmountIsDerivedFromTheFacilitysOwnDailyFee()
    {
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 5)), CancellationToken.None);

        // Never typed by the office. An LGU may change its fee mid-year, so a typed total would disagree with the rate
        // on record for those very days, and storing it would leave a figure arrears could not be reconciled against.
        var expected = added.Sum(a => a.TotalCollected);
        Assert.Equal(expected, result.Value!.TotalRecorded);
        Assert.True(expected > 0m);
    }

    [Fact]
    public async Task DaysAlreadyCollectedAreNotCollectedAgain()
    {
        // Three days of that month are already on record. A count of five must settle five OTHER days, not overwrite
        // these and report five.
        var taken = new List<DailyCollection>();
        foreach (var day in new[] { 1, 2, 3 })
        {
            var dc = DailyCollection.Create(StallId, new DateOnly(Past.Year, Past.Month, day), "collector", 30m);
            dc.MarkPaid("OR-EARLIER", collectorId: null, fishKilos: null, updatedBy: "collector");
            taken.Add(dc);
        }

        var (handler, added) = Build(onRecord: taken);

        await handler.Handle(Command(Row(1, "1", Past.Year, Past.Month, 5)), CancellationToken.None);

        Assert.Equal(5, added.Count);
        Assert.DoesNotContain(added, a => a.CollectionDate.Day is 1 or 2 or 3);
        Assert.All(taken, t => Assert.Equal("OR-EARLIER", t.ORNumber));
    }

    [Fact]
    public async Task ADayTheMarketWasClosedIsNeverSettled()
    {
        // A facility-wide closure owes nothing. Settling it would collect a fee for a day the market did not open.
        var closed = new DateOnly(Past.Year, Past.Month, 2);
        var (handler, added) = Build(closures: new[] { closed });

        await handler.Handle(Command(Row(1, "1", Past.Year, Past.Month, 4)), CancellationToken.None);

        Assert.Equal(4, added.Count);
        Assert.DoesNotContain(added, a => a.CollectionDate == closed);
    }

    [Fact]
    public async Task AnExcusedDayIsLeftExcused()
    {
        // An absent day is not owed at all - no ₱0 due, no later payment. Settling it would charge a vendor for a day
        // the office had already excused.
        var absentDate = new DateOnly(Past.Year, Past.Month, 1);
        var absent = DailyCollection.Create(StallId, absentDate, "head", 30m);
        absent.MarkAbsent("head");

        var (handler, added) = Build(onRecord: new[] { absent });

        await handler.Handle(Command(Row(1, "1", Past.Year, Past.Month, 3)), CancellationToken.None);

        Assert.DoesNotContain(added, a => a.CollectionDate == absentDate);
        Assert.True(absent.IsAbsent);
        Assert.False(absent.IsPaid);
    }

    [Fact]
    public async Task TheSameSpaceAndMonthTwiceInOneFileDoesNotSettleTheSameDaysTwice()
    {
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 4), Row(2, "1", Past.Year, Past.Month, 3)),
            CancellationToken.None);

        // Seven distinct days, not four days settled twice. Without the in-batch guard the second row re-reads the
        // month, finds the first row's days unsaved, and claims them again.
        Assert.Equal(7, added.Count);
        Assert.Equal(7, added.Select(a => a.CollectionDate).Distinct().Count());
        Assert.Equal(7, result.Value!.TotalDaysSettled);
    }

    [Fact]
    public async Task AMonthWithFewerCollectableDaysThanClaimedIsReportedAsPartNotAsDone()
    {
        // Every day of the month is already collected except two. A row claiming ten must say what it actually did,
        // or the office reconciles against a success that never happened.
        var daysInMonth = DateTime.DaysInMonth(Past.Year, Past.Month);
        var taken = new List<DailyCollection>();
        for (var day = 1; day <= daysInMonth - 2; day++)
        {
            var dc = DailyCollection.Create(StallId, new DateOnly(Past.Year, Past.Month, day), "collector", 30m);
            dc.MarkPaid("OR-EARLIER", collectorId: null, fishKilos: null, updatedBy: "collector");
            taken.Add(dc);
        }

        var (handler, added) = Build(onRecord: taken);

        var result = await handler.Handle(Command(Row(1, "1", Past.Year, Past.Month, 10)), CancellationToken.None);

        Assert.Equal(2, added.Count);
        Assert.Equal(ImportDailyOutcome.RecordedInPart, result.Value!.Results[0].Outcome);
        Assert.Equal(10, result.Value!.Results[0].DaysClaimed);
        Assert.Equal(2, result.Value!.Results[0].DaysSettled);
        Assert.Contains("Only 2 of 10", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task NoDayAfterTodayIsEverSettled()
    {
        // The month in progress is allowed - its days so far are real collection days - but a day that has not
        // happened cannot have been collected.
        var today = PhilippineTime.Today;
        var (handler, added) = Build();

        await handler.Handle(Command(Row(1, "1", today.Year, today.Month, 31)), CancellationToken.None);

        Assert.NotEmpty(added);
        Assert.All(added, a => Assert.True(a.CollectionDate <= today));
    }

    [Fact]
    public async Task AMonthThatHasNotStartedYetIsRefused()
    {
        var next = PhilippineTime.Today.AddMonths(2);
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(Row(1, "1", next.Year, next.Month, 5)), CancellationToken.None);

        Assert.Empty(added);
        Assert.Equal(1, result.Value!.RejectedCount);
        Assert.Contains("has not started yet", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task AnOrNumberIsRequiredAndIsWrittenOntoEveryDayItCovers()
    {
        var (handler, added) = Build();

        var refused = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 3, or: null)), CancellationToken.None);
        Assert.Empty(added);
        Assert.Equal(1, refused.Value!.RejectedCount);

        // One receipt covers the days it names, which is how the market's own collection dialog already records a run
        // of days. Without the OR on each day, every one of them is reported as missing a receipt.
        await handler.Handle(Command(Row(1, "1", Past.Year, Past.Month, 3, or: "OR-2002")), CancellationToken.None);
        Assert.Equal(3, added.Count);
        Assert.All(added, a => Assert.Equal("OR-2002", a.ORNumber));
    }

    [Fact]
    public async Task ADayNoTermAnswersForIsNeverSettled()
    {
        // The space was let only from the middle of the month, so the days before it are nobody's to owe.
        var start = new DateOnly(Past.Year, Past.Month, 15);
        var stall = Stall.Create(Guid.NewGuid(), "1", 0m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Id))!.SetValue(stall, StallId);
        stall.Contracts.Add(Contract.Create(stall.Id, "Kim Chui", "Kim Chui", start, durationYears: 3, monthlyRate: 900m));

        var (handler, added) = Build(stall);

        await handler.Handle(Command(Row(1, "1", Past.Year, Past.Month, 5)), CancellationToken.None);

        Assert.NotEmpty(added);
        Assert.All(added, a => Assert.True(a.CollectionDate >= start));
    }

    [Fact]
    public async Task DatesTheOfficeStatesAreSettledExactly()
    {
        // The office knows which days it collected, so those are the days recorded - not a run from the first of the
        // month. This is the whole point of stating them.
        var (handler, added) = Build();

        var dates = new[]
        {
            new DateOnly(Past.Year, Past.Month, 7),
            new DateOnly(Past.Year, Past.Month, 14),
            new DateOnly(Past.Year, Past.Month, 21)
        };

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 3) with { Days = dates.Select(d => new ImportDailyDay(d)).ToList() }), CancellationToken.None);

        Assert.Equal(dates, added.Select(a => a.CollectionDate).OrderBy(d => d));
        Assert.Equal(ImportDailyOutcome.RecordedInFull, result.Value!.Results[0].Outcome);
    }

    [Fact]
    public async Task AStatedDateThatCannotBeSettledIsNamedAndNotSubstituted()
    {
        // A closure among the stated days. Quietly settling a different day instead would invent a collection the
        // office never recorded, so the day is refused and named.
        var closed = new DateOnly(Past.Year, Past.Month, 14);
        var (handler, added) = Build(closures: new[] { closed });

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 2) with
            {
                Days = new[] { new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 7)), new ImportDailyDay(closed) }
            }),
            CancellationToken.None);

        Assert.Single(added);
        Assert.Equal(new DateOnly(Past.Year, Past.Month, 7), added[0].CollectionDate);
        Assert.Equal(ImportDailyOutcome.RecordedInPart, result.Value!.Results[0].Outcome);
        Assert.Contains("market closure", result.Value!.Results[0].Error);
        Assert.Contains($"{closed:yyyy-MM-dd}", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task StatedDatesAreNeverToppedUpToTheClaimedCount()
    {
        // Two dates given against a claim of five. The office stated the days; filling the other three from the
        // month's calendar would record collections nobody wrote down.
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 5) with
            {
                Days = new[] { new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 3)), new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 4)) }
            }),
            CancellationToken.None);

        Assert.Equal(2, added.Count);
        Assert.Equal(2, result.Value!.Results[0].DaysSettled);
        Assert.Equal(5, result.Value!.Results[0].DaysClaimed);
    }

    [Fact]
    public async Task ADateOutsideTheRowsMonthIsRefused()
    {
        // A transcription slip that would otherwise write a collection into a month the row does not name, where no
        // one reconciling that period would ever find it.
        var wrongMonth = Past.AddMonths(-1);
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 1) with
            {
                Days = new[] { new ImportDailyDay(new DateOnly(wrongMonth.Year, wrongMonth.Month, 5)) }
            }),
            CancellationToken.None);

        Assert.Empty(added);
        Assert.Contains("is not in", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task ADateGivenTwiceIsSettledOnce()
    {
        var (handler, added) = Build();
        var date = new DateOnly(Past.Year, Past.Month, 9);

        await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 2) with { Days = new[] { new ImportDailyDay(date), new ImportDailyDay(date) } }),
            CancellationToken.None);

        // One day, one fee. The same date twice would collect twice for a day the vendor paid for once.
        Assert.Single(added);
    }

    [Fact]
    public async Task AStatedDateIsHeldToTheSameRulesAsAFilledOne()
    {
        // Named by the office, but already collected. A stated date must not slip past a guard a filled day would
        // meet - that is why both go through one settle path.
        var taken = DailyCollection.Create(StallId, new DateOnly(Past.Year, Past.Month, 6), "collector", 30m);
        taken.MarkPaid("OR-EARLIER", collectorId: null, fishKilos: null, updatedBy: "collector");

        var (handler, added) = Build(onRecord: new[] { taken });

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 1) with
            {
                Days = new[] { new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 6)) }
            }),
            CancellationToken.None);

        Assert.Empty(added);
        Assert.Equal("OR-EARLIER", taken.ORNumber);
        Assert.Contains("already collected", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task ADayCanCarryItsOwnReceipt_AndFallsBackToTheRowsWhenItDoesNot()
    {
        // The market issues a receipt per collection, so a month's days may sit under several. Reducing them to one
        // would discard receipts the office can later be asked to produce.
        var (handler, added) = Build();

        await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 3, or: "OR-MONTH") with
            {
                Days = new[]
                {
                    new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 2), "OR-AAA"),
                    new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 3), "OR-BBB"),
                    new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 4))
                }
            }),
            CancellationToken.None);

        Assert.Equal(3, added.Count);
        Assert.Equal("OR-AAA", added.Single(a => a.CollectionDate.Day == 2).ORNumber);
        Assert.Equal("OR-BBB", added.Single(a => a.CollectionDate.Day == 3).ORNumber);

        // Blank takes the row's own receipt. Never left empty: a collection without one is reported as missing a
        // receipt by every arrears list that reads it.
        Assert.Equal("OR-MONTH", added.Single(a => a.CollectionDate.Day == 4).ORNumber);
    }

    [Fact]
    public async Task ARowWhoseDaysEachCarryAReceiptIsRecorded_EvenWithNoReceiptOnTheRow()
    {
        // Exactly what the office did: left the month's OR blank and wrote a receipt against each day, because that is
        // how the market is collected. The row was REJECTED for having no OR of its own and nothing was recorded - the
        // import asked for a figure the sheet does not keep, and refused the receipts it does.
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 2, or: null) with
            {
                Days = new[]
                {
                    new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 9), "77756724"),
                    new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 10), "6454645")
                }
            }),
            CancellationToken.None);

        Assert.Equal(2, added.Count);
        Assert.Equal(0, result.Value!.RejectedCount);
        Assert.Equal("77756724", added.Single(a => a.CollectionDate.Day == 9).ORNumber);
        Assert.Equal("6454645", added.Single(a => a.CollectionDate.Day == 10).ORNumber);
    }

    [Fact]
    public async Task ADayWithNoReceiptOfItsOwnAndNoneOnTheRowIsRefused()
    {
        // The other half of the same rule: a collection with no receipt anywhere is reported as missing one by every
        // arrears list that reads it, so it is refused rather than written.
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 2, or: null) with
            {
                Days = new[]
                {
                    new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 9), "77756724"),
                    new ImportDailyDay(new DateOnly(Past.Year, Past.Month, 10))
                }
            }),
            CancellationToken.None);

        Assert.Single(added);
        Assert.Equal(9, added[0].CollectionDate.Day);
        Assert.Contains("no OR number", result.Value!.Results[0].Error);
    }

    [Fact]
    public async Task AMonthlyBilledFacilityIsRefusedWholesale()
    {
        // A month there is a payment, not a run of days. Recording it through this path would settle days nobody
        // collected.
        var monthly = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var (handler, added) = Build(facility: monthly);

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 5)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(added);
    }

    [Fact]
    public async Task MoreDaysThanAnyMonthHoldsIsRejectedRatherThanClamped()
    {
        var (handler, added) = Build();

        var result = await handler.Handle(
            Command(Row(1, "1", Past.Year, Past.Month, 45)), CancellationToken.None);

        // Clamping would report success on a transcription error, and the office would reconcile against 31 days it
        // never wrote down.
        Assert.Empty(added);
        Assert.Equal(1, result.Value!.RejectedCount);
        Assert.Contains("more than any month holds", result.Value!.Results[0].Error);
    }
}
