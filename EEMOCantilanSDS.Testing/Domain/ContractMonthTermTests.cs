using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A Custom Space Rental stall contract may run for months as well as years. The term is Years × 12 + Months calendar months;
/// every yearly contract (months null) is read exactly as before.
/// </summary>
public class ContractMonthTermTests
{
    private static Contract Term(DateOnly start, int years, int? months = null) =>
        Contract.Create(Guid.NewGuid(), "Cold Storage Holder", "Cold Storage Holder", start, years, 5000m, durationMonths: months);

    [Fact]
    public void ASixMonthTermIsStoredAsZeroYearsAndSixMonthsAndBillsExactlySixMonths()
    {
        var term = Term(new DateOnly(2026, 3, 10), 0, 6);

        Assert.Equal((0, (int?)6, 6), (term.DurationYears, term.DurationMonths, term.TermMonths));
        Assert.Equal(new DateOnly(2026, 9, 9), term.ExpiryDate);          // the day before the six-month anniversary
        var billed = Enumerable.Range(0, 24).Select(i => new DateOnly(2026, 1, 1).AddMonths(i)).Where(m => term.BillsCalendarMonth(m.Year, m.Month)).ToList();
        Assert.Equal(6, billed.Count);
        Assert.Equal(new DateOnly(2026, 3, 1), billed.First());
        Assert.Equal(new DateOnly(2026, 8, 1), billed.Last());
    }

    [Fact]
    public void TwelveMonthsIsTheSameRecordAsOneYear_AndEighteenMonthsIsOneYearSixMonths()
    {
        var twelve = Term(new DateOnly(2026, 3, 10), 0, 12);
        Assert.Equal((1, (int?)null), (twelve.DurationYears, twelve.DurationMonths));
        var eighteen = Term(new DateOnly(2026, 3, 10), 0, 18);
        Assert.Equal((1, (int?)6), (eighteen.DurationYears, eighteen.DurationMonths));
        Assert.Equal(18, eighteen.TermMonths);
    }

    [Theory]
    [InlineData(2026, 1, 31, 1, 2026, 2, 27)]     // month-end start clamps to Feb 28, so the last day inside is Feb 27
    [InlineData(2027, 1, 31, 1, 2027, 2, 27)]
    [InlineData(2028, 1, 31, 1, 2028, 2, 28)]     // leap year: Jan 31 + 1 month = Feb 29, last day Feb 28
    [InlineData(2026, 8, 31, 6, 2027, 2, 27)]     // Aug 31 + 6 months = Feb 28
    [InlineData(2026, 2, 15, 6, 2026, 8, 14)]     // mid-month starts keep their day
    [InlineData(2028, 2, 29, 12, 2029, 2, 27)]    // Feb 29 + 12 months (one year) clamps to Feb 28
    public void TheLastDayInsideTheTermIsTheDayBeforeTheClampedAnniversary(int y, int m, int d, int months, int ey, int em, int ed)
    {
        var term = Term(new DateOnly(y, m, d), 0, months);
        Assert.Equal(new DateOnly(ey, em, ed), term.ExpiryDate);
        Assert.False(term.IsExpiredOn(new DateOnly(ey, em, ed)));
        Assert.True(term.IsExpiredOn(new DateOnly(ey, em, ed).AddDays(1)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void AYearlyContractIsUnchanged_ExpiryBillingAndExpiryTest(int years)
    {
        var yearly = Term(new DateOnly(2023, 6, 7), years);
        var asMonths = Term(new DateOnly(2023, 6, 7), 0, years * 12);       // normalises to the same record

        Assert.Null(yearly.DurationMonths);
        Assert.Equal(DomainRules.TermLastDay(new DateOnly(2023, 6, 7), years), yearly.ExpiryDate);
        Assert.Equal(yearly.ExpiryDate, asMonths.ExpiryDate);
        for (var m = new DateOnly(2022, 1, 1); m < new DateOnly(2035, 1, 1); m = m.AddMonths(1))
            Assert.Equal(yearly.BillsCalendarMonth(m.Year, m.Month), asMonths.BillsCalendarMonth(m.Year, m.Month));
    }

    [Fact]
    public void ALongYearsAndMonthsTermBillsEveryMonthOnceAndNoMore()
    {
        var term = Term(new DateOnly(2026, 1, 31), 2, 5);                    // 29 months
        var billed = 0;
        for (var m = new DateOnly(2025, 1, 1); m < new DateOnly(2032, 1, 1); m = m.AddMonths(1))
            if (term.BillsCalendarMonth(m.Year, m.Month)) billed++;
        Assert.Equal(29, billed);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, null)]
    public void ASignedContractWithNoTermIsStillRefused(int years, int? months) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Term(new DateOnly(2026, 3, 1), years, months));

    [Fact]
    public void AMonthTermBeyondFiftyYearsIsRefused() =>
        Assert.Throws<ArgumentException>(() => Term(new DateOnly(2026, 3, 1), 50, 6));

    [Fact]
    public void AnUnsignedOccupancyHasNoTermWhateverMonthsArrive()
    {
        var space = Contract.Create(Guid.NewGuid(), "Holder", null, new DateOnly(2026, 3, 1), 0, 5000m,
            arrangement: OccupancyArrangement.SpaceOnly, durationMonths: 6);
        Assert.Null(space.DurationMonths);
        Assert.Equal(DomainRules.OpenEndedTermYears, space.DurationYears);
        Assert.False(space.IsExpiredOn(new DateOnly(2090, 1, 1)));
    }

    [Fact]
    public void UpdatingTheTermCanSwitchBetweenYearsAndMonthsButNeverToNothing()
    {
        var term = Term(new DateOnly(2026, 3, 10), 3);
        term.UpdateTerms(new DateOnly(2026, 3, 10), 0, "head", 9);
        Assert.Equal((0, (int?)9), (term.DurationYears, term.DurationMonths));
        term.UpdateTerms(new DateOnly(2026, 3, 10), 2, "head");
        Assert.Equal((2, (int?)null), (term.DurationYears, term.DurationMonths));
        Assert.Throws<ArgumentOutOfRangeException>(() => term.UpdateTerms(new DateOnly(2026, 3, 10), 0, "head"));
    }
}
