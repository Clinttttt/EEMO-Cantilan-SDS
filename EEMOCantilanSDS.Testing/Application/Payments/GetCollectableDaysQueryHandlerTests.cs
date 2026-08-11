using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Queries.Payments.GetCollectableDays;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Payments;

/// <summary>
/// The market days of one month a stall still owes.
///
/// <para>
/// This exists so the collection-history import can offer a payor's OWN uncollected days instead of the first days of
/// the calendar. A space let on the 9th owes nothing for the 1st, and a day already collected cannot be collected
/// again — offering either invites the office to record something that is not true.
/// </para>
///
/// <para>The rules are the import's own, so what is offered and what is accepted cannot drift.</para>
/// </summary>
public class GetCollectableDaysQueryHandlerTests
{
    private static readonly Guid StallId = Guid.NewGuid();
    private static readonly DateOnly Past = PhilippineTime.Today.AddMonths(-6);

    /// <summary>A market space let from the given day of the month under test.</summary>
    private static Stall StallLetFrom(int day)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", 0m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Id))!.SetValue(stall, StallId);

        stall.Contracts.Add(Contract.Create(
            stall.Id, "Kim Chui", "Kim Chui",
            new DateOnly(Past.Year, Past.Month, day), durationYears: 3, monthlyRate: 900m));

        return stall;
    }

    private static GetCollectableDaysQueryHandler Build(
        Stall stall,
        IEnumerable<DailyCollection>? onRecord = null,
        IEnumerable<DateOnly>? closures = null)
    {
        var stalls = new Mock<IStallRepository>();
        stalls.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);

        var existing = (onRecord ?? Array.Empty<DailyCollection>()).ToList();
        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(d => d.GetByStallAndMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int y, int m, CancellationToken _) =>
                existing.Where(e => e.CollectionDate.Year == y && e.CollectionDate.Month == m).ToList());

        var closureList = (closures ?? Array.Empty<DateOnly>()).ToList();
        var closureRepo = new Mock<INpmMarketClosureRepository>();
        closureRepo.Setup(c => c.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int y, int m, CancellationToken _) =>
                closureList.Where(d => d.Year == y && d.Month == m)
                           .Select(d => NpmMarketClosure.Create(d))
                           .ToList());

        return new GetCollectableDaysQueryHandler(stalls.Object, daily.Object, closureRepo.Object);
    }

    private static DailyCollection Collected(int day)
    {
        var dc = DailyCollection.Create(StallId, new DateOnly(Past.Year, Past.Month, day), "collector", 30m);
        dc.MarkPaid("OR-1", collectorId: null, fishKilos: null, updatedBy: "collector");
        return dc;
    }

    [Fact]
    public async Task DaysBeforeTheSpaceWasLetAreNotOwed()
    {
        // The very case that made a calendar-order prefill wrong: a space let on the 9th owes nothing for the 1st.
        var handler = Build(StallLetFrom(9));

        var result = await handler.Handle(new GetCollectableDaysQuery(StallId, Past.Year, Past.Month), CancellationToken.None);

        Assert.NotEmpty(result.Value!.Uncollected);
        Assert.All(result.Value!.Uncollected, d => Assert.True(d.Day >= 9));
        Assert.Equal(8, result.Value!.ClosedOrOutsideTerm);
    }

    [Fact]
    public async Task ADayAlreadyCollectedIsNotOfferedAgain()
    {
        var handler = Build(StallLetFrom(1), onRecord: new[] { Collected(1), Collected(2) });

        var result = await handler.Handle(new GetCollectableDaysQuery(StallId, Past.Year, Past.Month), CancellationToken.None);

        Assert.DoesNotContain(new DateOnly(Past.Year, Past.Month, 1), result.Value!.Uncollected);
        Assert.DoesNotContain(new DateOnly(Past.Year, Past.Month, 2), result.Value!.Uncollected);
        Assert.Equal(2, result.Value!.AlreadyCollected);
    }

    [Fact]
    public async Task AnExcusedDayIsNeitherOwedNorOffered()
    {
        var absent = DailyCollection.Create(StallId, new DateOnly(Past.Year, Past.Month, 3), "head", 30m);
        absent.MarkAbsent("head");

        var handler = Build(StallLetFrom(1), onRecord: new[] { absent });

        var result = await handler.Handle(new GetCollectableDaysQuery(StallId, Past.Year, Past.Month), CancellationToken.None);

        Assert.DoesNotContain(new DateOnly(Past.Year, Past.Month, 3), result.Value!.Uncollected);
        Assert.Equal(1, result.Value!.Excused);
    }

    [Fact]
    public async Task ADayTheMarketDidNotOpenIsNotOwed()
    {
        var closed = new DateOnly(Past.Year, Past.Month, 4);
        var handler = Build(StallLetFrom(1), closures: new[] { closed });

        var result = await handler.Handle(new GetCollectableDaysQuery(StallId, Past.Year, Past.Month), CancellationToken.None);

        Assert.DoesNotContain(closed, result.Value!.Uncollected);
    }

    [Fact]
    public async Task DaysThatHaveNotHappenedAreNotOwedYet()
    {
        // The month in progress is normal. Its remaining days are simply not owed yet - counting them as closures would
        // misdescribe them, and offering them would record a collection for a day that has not come.
        var today = PhilippineTime.Today;
        var stall = Stall.Create(Guid.NewGuid(), "1", 0m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Id))!.SetValue(stall, StallId);
        stall.Contracts.Add(Contract.Create(stall.Id, "Kim Chui", "Kim Chui",
            today.AddYears(-1), durationYears: 3, monthlyRate: 900m));

        var handler = Build(stall);

        var result = await handler.Handle(new GetCollectableDaysQuery(StallId, today.Year, today.Month), CancellationToken.None);

        Assert.All(result.Value!.Uncollected, d => Assert.True(d <= today));
    }

    [Fact]
    public async Task TheDaysComeBackEarliestFirst()
    {
        // The import fills lines in the order given, so an unordered answer would date the office's first line with a
        // day from the middle of the month.
        var handler = Build(StallLetFrom(1), onRecord: new[] { Collected(2), Collected(5) });

        var result = await handler.Handle(new GetCollectableDaysQuery(StallId, Past.Year, Past.Month), CancellationToken.None);

        Assert.Equal(result.Value!.Uncollected.OrderBy(d => d), result.Value!.Uncollected);
    }

    [Fact]
    public async Task TheChargeableDaysAreThePayorsOwn_CollectedOnesIncluded()
    {
        // A space let on the 9th, with the 9th and 10th already collected. Its chargeable days are 9, 10, 11... - so
        // the 11th is the payor's THIRD market day, which is the number the office's own reckoning uses. Numbering an
        // entry form's lines 1, 2, 3 described the form rather than the vendor.
        var handler = Build(StallLetFrom(9), onRecord: new[] { Collected(9), Collected(10) });

        var result = await handler.Handle(new GetCollectableDaysQuery(StallId, Past.Year, Past.Month), CancellationToken.None);

        var chargeable = result.Value!.Chargeable!;
        Assert.Equal(new DateOnly(Past.Year, Past.Month, 9), chargeable[0]);
        Assert.Equal(new DateOnly(Past.Year, Past.Month, 10), chargeable[1]);
        Assert.Equal(new DateOnly(Past.Year, Past.Month, 11), chargeable[2]);

        // Ordered, and a superset of what is still owed: the collected days are the payor's days too, and dropping them
        // would renumber every day after them.
        Assert.Equal(chargeable.OrderBy(d => d), chargeable);
        Assert.All(result.Value!.Uncollected, d => Assert.Contains(d, chargeable));
    }

    [Fact]
    public async Task AnExcusedDayIsNotOneOfThePayorsDaysToCount()
    {
        // Nothing is owed for an excused day, so it is not the payor's day either. Counting it would push every later
        // day's number up by one and disagree with the office's own reckoning.
        var absent = DailyCollection.Create(StallId, new DateOnly(Past.Year, Past.Month, 2), "head", 30m);
        absent.MarkAbsent("head");

        var handler = Build(StallLetFrom(1), onRecord: new[] { absent });

        var result = await handler.Handle(new GetCollectableDaysQuery(StallId, Past.Year, Past.Month), CancellationToken.None);

        Assert.DoesNotContain(new DateOnly(Past.Year, Past.Month, 2), result.Value!.Chargeable!);
        Assert.Equal(new DateOnly(Past.Year, Past.Month, 1), result.Value!.Chargeable![0]);
        Assert.Equal(new DateOnly(Past.Year, Past.Month, 3), result.Value!.Chargeable![1]);
    }

    [Fact]
    public async Task AMonthThatIsNotAMonthIsRefused()
    {
        var handler = Build(StallLetFrom(1));

        var result = await handler.Handle(new GetCollectableDaysQuery(StallId, Past.Year, 13), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
