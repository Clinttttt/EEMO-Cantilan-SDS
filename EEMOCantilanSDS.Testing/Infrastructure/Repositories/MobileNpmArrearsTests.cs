using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The collector's arrears screen reaches back past months, and prices each one as the office settles it.
/// </summary>
/// <remarks>
/// It used to show one calendar month, so a day missed in one month was invisible in the next and could not be collected from
/// the app at all. Reaching back is only half of it: a past month must be priced by the office's rule, not by counting days and
/// multiplying. Where a month is let for a rent it owes that rent whatever its calendar gave it, so a 31-day month at ₱30 owes
/// ₱900 and not ₱930.
/// </remarks>
public class MobileNpmArrearsTests : RepositoryTestBase
{
    private static readonly DateOnly Today = new(2026, 9, 15);

    private static (Facility Facility, Stall Stall, Contract Contract) Market(decimal monthlyRate = 900m)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", monthlyRate, ApplicableFees.BaseRental, MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Kim Chui", "Kim Chui", new DateOnly(2026, 7, 1), 3, monthlyRate);
        return (facility, stall, contract);
    }

    /// <summary>A day the office collected, at Cantilan's ordinance fee.</summary>
    private static DailyCollection Paid(Guid stallId, DateOnly day)
    {
        var collection = DailyCollection.Create(stallId, day);
        collection.MarkPaid("OR-1", null);
        return collection;
    }

    private static StallRepository Repo(AppDbContext context) => new(
        context,
        new global::EEMOCantilanSDS.Infrastructure.Fees.FeeRateResolver(context),
        new FixedClock(Today.ToDateTime(TimeOnly.MinValue).AddHours(-8)));

    /// <summary>
    /// A month that has closed owing is stated at the month's own rent, not at a day's fee times its calendar.
    /// </summary>
    [Fact]
    public async Task AClosedMonthIsPricedAtWhatTheOfficeSettlesItAt()
    {
        var context = NewContext();
        var (facility, stall, contract) = Market();
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var arrears = await Repo(context).GetMobileNpmArrearsAsync(Today.Year, Today.Month, Today, CancellationToken.None);

        var payor = Assert.Single(arrears.Payors);

        // July and August both closed with nothing collected. August has 31 days: at ₱30 that would count out to ₱930, but the
        // month was let for ₱900 and that is what it owes.
        var august = Assert.Single(payor.PastMonths, m => m is { Year: 2026, Month: 8 });
        Assert.Equal(900m, august.Amount);

        var july = Assert.Single(payor.PastMonths, m => m is { Year: 2026, Month: 7 });
        Assert.Equal(900m, july.Amount);
    }

    /// <summary>Past months are listed oldest first, which is the order arrears are collected in.</summary>
    [Fact]
    public async Task PastMonthsAreListedOldestFirst()
    {
        var context = NewContext();
        var (facility, stall, contract) = Market();
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var arrears = await Repo(context).GetMobileNpmArrearsAsync(Today.Year, Today.Month, Today, CancellationToken.None);

        var months = Assert.Single(arrears.Payors).PastMonths;

        Assert.Equal([(2026, 7), (2026, 8)], months.Select(m => (m.Year, m.Month)));
    }

    /// <summary>
    /// Today is not an arrear.
    /// </summary>
    /// <remarks>
    /// The daily round answers for today and every screen before this one already states it. Listing it here asked the collector
    /// to chase the very day he was standing at the stall to collect.
    /// </remarks>
    [Fact]
    public async Task TheDayInHandIsNotListedAmongTheDaysOwed()
    {
        var context = NewContext();
        var (facility, stall, contract) = Market();
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var arrears = await Repo(context).GetMobileNpmArrearsAsync(Today.Year, Today.Month, Today, CancellationToken.None);

        var payor = Assert.Single(arrears.Payors);

        Assert.DoesNotContain(Today, payor.DaysOwedThisMonth);
        // The 1st to the 14th are gone by and owed; the 15th is the round's own business.
        Assert.Equal(14, payor.DaysOwedThisMonth.Count);
        Assert.Equal(new DateOnly(2026, 9, 14), payor.DaysOwedThisMonth[^1]);
    }

    /// <summary>
    /// A short month owes its full rent, and the difference its calendar could not reach in daily installments.
    /// </summary>
    /// <remarks>
    /// This is the case that separates the rule from arithmetic. February 2026 has 28 days: at ₱30 the installments come to
    /// ₱840, ₱60 short of the ₱900 the month was let for, and once the month has closed that ₱60 is owed as a month-end
    /// difference. A screen that counted the days and multiplied would under-bill the office by ₱60 - and for a 31-day month it
    /// would over-bill by ₱30. The settlement service is asked instead, so the collector's screen states what the payor's own
    /// screen states.
    /// </remarks>
    [Fact]
    public async Task AShortMonthOwesItsRentAndNotJustItsDays()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.BaseRental, MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Brando Pol", "Brando Pol", new DateOnly(2026, 2, 1), 3, 900m);
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var march = new DateOnly(2026, 3, 15);
        var repo = new StallRepository(
            context,
            new global::EEMOCantilanSDS.Infrastructure.Fees.FeeRateResolver(context),
            new FixedClock(march.ToDateTime(TimeOnly.MinValue).AddHours(-8)));

        var arrears = await repo.GetMobileNpmArrearsAsync(march.Year, march.Month, march, CancellationToken.None);

        var february = Assert.Single(Assert.Single(arrears.Payors).PastMonths, m => m is { Year: 2026, Month: 2 });

        // The month owes its rent.
        Assert.Equal(900m, february.Amount);
        // And it is made of twenty-eight installments, not thirty: the remaining ₱60 is the month-end difference, which is why
        // the days cannot simply be multiplied.
        Assert.Equal(28, february.Days);
        Assert.NotEqual(february.Days * 30m, february.Amount);
    }

    /// <summary>A month settled in full is not an arrear, and does not appear.</summary>
    [Fact]
    public async Task AMonthAlreadyCollectedIsNotListed()
    {
        var context = NewContext();
        var (facility, stall, contract) = Market();
        context.AddRange(facility, stall, contract);

        // Every day of July collected.
        for (var d = new DateOnly(2026, 7, 1); d.Month == 7; d = d.AddDays(1))
            context.Add(Paid(stall.Id, d));

        await context.SaveChangesAsync();

        var arrears = await Repo(context).GetMobileNpmArrearsAsync(Today.Year, Today.Month, Today, CancellationToken.None);

        var payor = Assert.Single(arrears.Payors);

        Assert.DoesNotContain(payor.PastMonths, m => m is { Year: 2026, Month: 7 });
    }

    /// <summary>
    /// A month before the payor held the space is not theirs to owe.
    /// </summary>
    [Fact]
    public async Task MonthsBeforeTheTermAreNotOwed()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "2", 900m, ApplicableFees.BaseRental, MarketSection.VegetableArea);
        // Let from 1 August: July is nobody's debt on this stall.
        var contract = Contract.Create(stall.Id, "Justin Bieber", "Justin Bieber", new DateOnly(2026, 8, 1), 3, 900m);
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var arrears = await Repo(context).GetMobileNpmArrearsAsync(Today.Year, Today.Month, Today, CancellationToken.None);

        var payor = Assert.Single(arrears.Payors);

        Assert.DoesNotContain(payor.PastMonths, m => m.Month == 7);
        Assert.Contains(payor.PastMonths, m => m is { Year: 2026, Month: 8 });
    }

    /// <summary>A payor behind on nothing does not appear on the screen at all.</summary>
    [Fact]
    public async Task APayorWhoOwesNothingIsNotListed()
    {
        var context = NewContext();
        var (facility, stall, contract) = Market();
        context.AddRange(facility, stall, contract);

        // Everything from the start of the term up to yesterday collected.
        for (var d = new DateOnly(2026, 7, 1); d < Today; d = d.AddDays(1))
            context.Add(Paid(stall.Id, d));

        await context.SaveChangesAsync();

        var arrears = await Repo(context).GetMobileNpmArrearsAsync(Today.Year, Today.Month, Today, CancellationToken.None);

        Assert.Empty(arrears.Payors);
        Assert.Equal(0m, arrears.TotalOutstanding);
    }
}
