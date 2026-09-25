using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The office's own arithmetic for a year of rent, stated in pesos.
///
/// <para>
/// The Municipality of Cantilan's List of Stallholders for the New Public Market vegetable area gives, for every row:
/// effectivity 6/7/2023, a term of 3 yrs, 4.8 sq.m., ₱900.00 monthly per contract, and a WHOLE YEAR RENTAL OF ₱10,800.00.
/// That is twelve months exactly, with no thirteenth part-month. Ruled 2026-09-12; the document is kept at
/// docs/evidence/npm-vegetable-area-stallholder-list-2026-09-12.jpg.
/// </para>
///
/// <para>
/// The platform had the term running THROUGH the anniversary, which is a year plus one day. Monthly-billed accounts were
/// unaffected, because <see cref="Contract.BillsCalendarMonth"/> counts N × 12 calendar months of its own accord and never
/// consulted the expiry date. A DAILY-collected space is charged per market day up to and including the expiry, so it took
/// the extra day: a one-year ₱900 stall in Cantilan billed ₱10,800 plus a ₱30 day. The ₱30 is how the office noticed.
/// </para>
///
/// <para>
/// These tests state the money rather than the dates, so that a future change to the expiry formula has to answer to the
/// office's paper and not merely to another date arithmetic.
/// </para>
/// </summary>
public class WholeYearRentalRulingTests
{
    private const decimal MonthlyRent = 900m;

    // The effectivity every row of the office's list carries.
    private static readonly DateOnly Effectivity = new(2023, 6, 7);

    private static Contract Term(int years) =>
        Contract.Create(Guid.NewGuid(), "Ramil C. Orjeles", "Ramil C. Orjeles", Effectivity, years, MonthlyRent);

    private static int BillingMonths(Contract term, int years)
    {
        var count = 0;
        var month = new DateOnly(term.EffectivityDate.Year, term.EffectivityDate.Month, 1);

        // Walk a year beyond the term to prove it STOPS, rather than counting only where it was expected to bill.
        for (var i = 0; i < (years + 1) * 12; i++)
        {
            if (term.BillsCalendarMonth(month.Year, month.Month)) count++;
            month = month.AddMonths(1);
        }

        return count;
    }

    [Fact]
    public void AYearOfA900PesoSpaceIs10800_TwelveMonthsExactly()
    {
        var term = Term(1);

        Assert.Equal(12, BillingMonths(term, 1));
        Assert.Equal(10_800m, BillingMonths(term, 1) * MonthlyRent);
        Assert.Equal(10_800m, term.WholeYearRental);
    }

    [Fact]
    public void TheThreeYearTermOnTheOfficesListIs32400_ThirtySixMonthsExactly()
    {
        var term = Term(3);

        Assert.Equal(36, BillingMonths(term, 3));
        Assert.Equal(32_400m, BillingMonths(term, 3) * MonthlyRent);
    }

    [Fact]
    public void ADailyCollectedTermIsCollectableForExactlyAYearOfDays_NotOneDayMore()
    {
        var term = Term(1);

        var collectable = 0;
        for (var d = Effectivity; d <= Effectivity.AddYears(2); d = d.AddDays(1))
            if (term.IsCollectableOn(d)) collectable++;

        // 7 Jun 2023 through 6 Jun 2024 inclusive: every day up to, but not including, the anniversary. Stated as the
        // arithmetic rather than a number because this particular year spans February 2024 and so runs to 366 days — it
        // was 367 while the term ran THROUGH the anniversary, and that one extra day is the ₱30 the office queried.
        var aYearOfDays = Effectivity.AddYears(1).DayNumber - Effectivity.DayNumber;
        Assert.Equal(aYearOfDays, collectable);
        Assert.Equal(366, collectable);      // the leap day is real; the point is that it is not 367

        // Deliberately NOT asserted in pesos: a daily-collected month is capped at the monthly rent, so days × the daily
        // fee is not what the account is billed. The month ceiling has its own tests; this states only the day count.
    }

    [Fact]
    public void TheTermIsLiveOnItsLastDayAndExpiredOnTheAnniversary()
    {
        var term = Term(3);

        Assert.Equal(new DateOnly(2026, 6, 6), term.ExpiryDate);

        Assert.False(term.IsExpiredOn(new DateOnly(2026, 6, 6)));    // the last day is still inside the term
        Assert.True(term.IsCollectableOn(new DateOnly(2026, 6, 6)));

        Assert.True(term.IsExpiredOn(new DateOnly(2026, 6, 7)));     // the anniversary is outside it
        Assert.False(term.IsCollectableOn(new DateOnly(2026, 6, 7)));
    }

    [Fact]
    public void TheEntityAndTheSharedRuleStateTheSameLastDay()
    {
        // Contract.ComputeExpiry and DomainRules.TermHasExpired each carried their own copy of the arithmetic, which is how
        // they could differ by a day. Both read DomainRules.TermLastDay now, and this holds them to it.
        foreach (var years in new[] { 1, 3, 5, 10 })
        {
            var term = Term(years);

            Assert.Equal(DomainRules.TermLastDay(Effectivity, years), term.ExpiryDate);
            Assert.False(term.IsExpiredOn(term.ExpiryDate));
            Assert.True(term.IsExpiredOn(term.ExpiryDate.AddDays(1)));
        }
    }
}
