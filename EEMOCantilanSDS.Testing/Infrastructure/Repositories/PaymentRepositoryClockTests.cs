using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// What a clerk may take money for is a question about TODAY, so the answer has to be asked of a stated day.
///
/// <para>
/// NPM bills per market day, so an in-progress month is only partly owed: offering the whole of it would collect for days the
/// vendor has not yet occupied. The rule lives in <c>DomainRules.EarnedThrough</c> and the repository supplies "today".
/// </para>
///
/// <para>
/// These assertions were not possible while the repository read the machine clock. The existing daily-status tests show what
/// that cost: they build their data BACKWARDS from today and clamp it to the first of the month, with a comment explaining
/// that on the 1st or 2nd there are simply fewer days. That is a test working around its subject rather than describing it.
/// </para>
/// </summary>
public class PaymentRepositoryClockTests : RepositoryTestBase
{
    /// <summary>A repository that believes it is <paramref name="today"/>.</summary>
    private static PaymentRepository RepositoryAsOf(EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext context, DateOnly today)
    {
        IClock clock = new FixedClock(today.ToDateTime(TimeOnly.MinValue).AddHours(-8));   // 00:00 Philippine time
        return new PaymentRepository(context, new FeeRateResolver(context), clock);
    }

    /// <summary>An NPM stall let from the first of January, with nothing yet collected.</summary>
    private async Task<Guid> SeedNpmStallAsync(EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext context)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Pantom Dant", "Pantom Dant", new DateOnly(2026, 1, 1), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();
        return stall.Id;
    }

    [Fact]
    public async Task TheCurrentMonthIsBilledOnlyUpToToday()
    {
        var context = NewContext();
        var stallId = await SeedNpmStallAsync(context);

        // Standing on the 10th of March, the month is billed for ten elapsed days — not the whole month.
        //
        // The daily rate is derived from a CLOSED month rather than written down here: a Cantilan figure hardcoded in a test
        // is a Cantilan figure waiting to be asserted for another LGU. A closed month bills the office's reference month of
        // DomainRules.DailyBilledMonthDays installments, whatever the calendar length — which is why February divides by 30
        // and not by 28.
        var rows = await RepositoryAsOf(context, new DateOnly(2026, 3, 10))
            .GetOutstandingMonthsAsync(stallId, null, null, CancellationToken.None);

        var closedMonth = rows.Single(r => r.Period == "2026-02");
        var perDay = closedMonth.TotalBill / DomainRules.DailyBilledMonthDays;

        var march = rows.Single(r => r.Period == "2026-03");
        Assert.Equal(perDay * 10m, march.TotalBill);
    }

    [Fact]
    public async Task TheSameMonthOwesMoreLaterInTheMonth()
    {
        // The same stall and the same data, asked on two different days. Nothing about the office changed; only the date
        // did, and the amount collectable moved with it. This is what the static clock made impossible to state.
        var context = NewContext();
        var stallId = await SeedNpmStallAsync(context);

        var onTheFifth = await RepositoryAsOf(context, new DateOnly(2026, 3, 5))
            .GetOutstandingMonthsAsync(stallId, null, null, CancellationToken.None);
        var onTheTwentieth = await RepositoryAsOf(context, new DateOnly(2026, 3, 20))
            .GetOutstandingMonthsAsync(stallId, null, null, CancellationToken.None);

        var fifth = onTheFifth.Single(r => r.Period == "2026-03").TotalBill;
        var twentieth = onTheTwentieth.Single(r => r.Period == "2026-03").TotalBill;

        Assert.True(fifth > 0m, "five elapsed days should be chargeable");
        Assert.Equal(fifth / 5m * 20m, twentieth);
    }

    [Fact]
    public async Task AClosedMonthBillsTheOfficesReferenceMonthWhateverItsLength()
    {
        // February has 28 days and January 31, yet both bill the same closed-month figure: the office's paper states a
        // reference month of DomainRules.DailyBilledMonthDays installments, so a short month is not a discount and a long
        // one is not a penalty. Asserted here because it is the rule the elapsed-day arithmetic above is measured against.
        var context = NewContext();
        var stallId = await SeedNpmStallAsync(context);

        var inDecember = await RepositoryAsOf(context, new DateOnly(2026, 12, 31))
            .GetOutstandingMonthsAsync(stallId, null, null, CancellationToken.None);

        var january = inDecember.Single(r => r.Period == "2026-01").TotalBill;
        var february = inDecember.Single(r => r.Period == "2026-02").TotalBill;

        Assert.Equal(january, february);

        // And a closed month reads the same whenever it is asked about.
        var inMarch = await RepositoryAsOf(context, new DateOnly(2026, 3, 1))
            .GetOutstandingMonthsAsync(stallId, null, null, CancellationToken.None);

        Assert.Equal(february, inMarch.Single(r => r.Period == "2026-02").TotalBill);
    }

    [Fact]
    public async Task AMonthStillInTheFutureIsNotOfferedAtAll()
    {
        // Nothing is collectable for a month that has not started, so it must not appear as an outstanding period. Offering
        // it would let a clerk take money for days nobody has occupied.
        var context = NewContext();
        var stallId = await SeedNpmStallAsync(context);

        var rows = await RepositoryAsOf(context, new DateOnly(2026, 3, 10))
            .GetOutstandingMonthsAsync(stallId, null, null, CancellationToken.None);

        Assert.DoesNotContain(rows, r => r.Period == "2026-04");
        Assert.DoesNotContain(rows, r => string.CompareOrdinal(r.Period, "2026-03") > 0);
    }
}
