using EEMOCantilanSDS.Domain.Entities.Facilities;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A monthly-billed term of N years owes exactly N × 12 months' rent, whatever day of the month it began on.
/// <para>
/// The obligation used to bill every calendar month the term OVERLAPPED. A term running 7 June 2023 to 7 June 2026
/// touches thirty-seven calendar months, so both part-Junes were charged whole and a three-year term read ₱33,300
/// where ₱32,400 was owed — every monthly-billed account in the office over-stated by one month's rent, on the
/// register, the arrears lists and the printed reports alike.
/// </para>
/// <para>Daily-collected market spaces do not use this rule: they are charged per market day.</para>
/// </summary>
public class ContractBillingMonthsTests
{
    private static Contract Term(DateOnly start, int years) =>
        Contract.Create(Guid.NewGuid(), "Merlita A. Abuso", "Merlita A. Abuso", start, years, 900m);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void ATermOwesTwelveMonthsPerYear_WhicheverDayItStartsOn(int years)
    {
        // Counted across a generous span so nothing outside the term can be missed, on a start day that guarantees
        // both the first and last calendar months are partial — the case the old rule double-counted.
        var start = new DateOnly(2023, 6, 7);
        var term = Term(start, years);

        var billed = 0;
        for (var m = new DateOnly(2020, 1, 1); m < new DateOnly(2035, 1, 1); m = m.AddMonths(1))
            if (term.BillsCalendarMonth(m.Year, m.Month)) billed++;

        Assert.Equal(years * 12, billed);
    }

    [Fact]
    public void TheBillingMonthsRunFromEffectivityToTheMonthBeforeTheAnniversary()
    {
        // 7 June 2023 for three years: June 2023 is the first billing month and May 2026 the last, because the last
        // month of the term ends on 6 June 2026 — the day before the anniversary.
        var term = Term(new DateOnly(2023, 6, 7), 3);

        Assert.True(term.BillsCalendarMonth(2023, 6), "the month of effectivity is billed");
        Assert.True(term.BillsCalendarMonth(2026, 5), "the month before the anniversary is the last billed");
        Assert.False(term.BillsCalendarMonth(2026, 6), "the anniversary month is NOT a thirty-seventh month");
        Assert.False(term.BillsCalendarMonth(2023, 5), "nothing is owed before the term began");
    }

    [Fact]
    public void ATermStartingOnTheFirst_RunsToTheEndOfItsFinalYear()
    {
        // The clean case, kept alongside the awkward one: 1 January for one year is January to December.
        var term = Term(new DateOnly(2024, 1, 1), 1);

        Assert.True(term.BillsCalendarMonth(2024, 1));
        Assert.True(term.BillsCalendarMonth(2024, 12));
        Assert.False(term.BillsCalendarMonth(2025, 1), "January of the next year belongs to the next term");
    }

    [Fact]
    public void ATermWithNoStatedDuration_OwesNothing()
    {
        // Rather than guess a length: a term with no years recorded is a data fault, and inventing rent for it would
        // put a figure on a demand letter that no contract supports.
        Assert.False(Term(new DateOnly(2024, 1, 1), 0).BillsCalendarMonth(2024, 1));
    }
}
