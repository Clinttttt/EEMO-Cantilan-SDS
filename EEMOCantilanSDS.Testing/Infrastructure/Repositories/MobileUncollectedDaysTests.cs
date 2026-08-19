using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using System.Reflection;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Which elapsed days a market payor still owes - the list the collector's app offers when settling a day the office missed.
///
/// <para>
/// A different question from "how much of this month does this occupancy owe", which the printed register asks and
/// CountCollectableDays answers with a plain span subtraction. That subtraction knows nothing about market closures or about
/// which days are already on record, so it can be larger than what can actually be collected - and a collector acting on it
/// would be sent to ask a payor for money on a day the market did not open.
/// </para>
///
/// <para>
/// Exercised through the private helper by reflection: it is repository-internal arithmetic with no seam of its own, and
/// standing up a database to reach it would test EF rather than the rule.
/// </para>
/// </summary>
public class MobileUncollectedDaysTests
{
    private static readonly DateOnly MonthStart = new(2026, 8, 1);

    /// <summary>A stall on one term that began before this month, with the given days already on record.</summary>
    private static Stall StallWith(params (DateOnly Date, bool Paid, bool Absent)[] recorded) =>
        StallFrom(MonthStart.AddMonths(-1), recorded);

    private static Stall StallFrom(DateOnly termStart, params (DateOnly Date, bool Paid, bool Absent)[] recorded)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", 900m, ApplicableFees.DailyRental,
            section: MarketSection.VegetableArea, dailyRate: 30m);

        stall.Contracts.Add(Contract.Create(stall.Id, "Payor", "Payor", termStart, 3, 900m));

        foreach (var (date, paid, absent) in recorded)
        {
            var collection = DailyCollection.Create(stall.Id, date);
            if (paid) collection.MarkPaid(string.Empty, collectorId: null);
            if (absent) collection.MarkAbsent();
            stall.DailyCollections.Add(collection);
        }

        return stall;
    }

    private static List<DateOnly> Owed(Stall stall, DateOnly effectiveEnd, params DateOnly[] closures)
    {
        var method = typeof(StallRepository).GetMethod("UncollectedDays",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return (List<DateOnly>)method.Invoke(null,
            [stall, MonthStart, effectiveEnd, new HashSet<DateOnly>(closures)])!;
    }

    [Fact]
    public void EveryElapsedDayIsOwedWhenNothingIsRecorded()
    {
        var owed = Owed(StallWith(), new DateOnly(2026, 8, 3));

        Assert.Equal([new(2026, 8, 1), new(2026, 8, 2), new(2026, 8, 3)], owed);
    }

    [Fact]
    public void ADayTheMarketWasCLOSEDIsOwedByNobody()
    {
        // The reason this exists rather than reusing the register's day count: that count includes the 2nd, and a collector
        // would have been sent to collect for a day the market did not open.
        var owed = Owed(StallWith(), new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 2));

        Assert.Equal([new(2026, 8, 1), new(2026, 8, 3)], owed);
    }

    [Fact]
    public void ADayAlreadyPaidIsNotOwedAgain()
    {
        var owed = Owed(StallWith((new DateOnly(2026, 8, 2), true, false)), new DateOnly(2026, 8, 3));

        Assert.Equal([new(2026, 8, 1), new(2026, 8, 3)], owed);
    }

    [Fact]
    public void ADayALREADYEXCUSEDIsNotOwedEither()
    {
        // An excused absence is settled business: the payor was not operating and owes nothing for it.
        var owed = Owed(StallWith((new DateOnly(2026, 8, 2), false, true)), new DateOnly(2026, 8, 3));

        Assert.Equal([new(2026, 8, 1), new(2026, 8, 3)], owed);
    }

    [Fact]
    public void DaysBeforeTheTermBeganAreNotOwed()
    {
        // A space let on the 9th owes nothing for the 1st - the same case that made a calendar-order prefill wrong on the
        // portal's import screen.
        var owed = Owed(StallFrom(new DateOnly(2026, 8, 9)), new DateOnly(2026, 8, 10));

        Assert.Equal([new(2026, 8, 9), new(2026, 8, 10)], owed);
    }

    [Fact]
    public void NOTHINGIsOwedBeforeTheMonthHasStarted()
    {
        // A future month: the effective end sits before its first day, and a list of days would be a claim about days that
        // have not happened.
        var owed = Owed(StallWith(), MonthStart.AddDays(-1));

        Assert.Empty(owed);
    }
}
