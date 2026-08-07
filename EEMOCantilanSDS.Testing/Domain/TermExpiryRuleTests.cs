using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The single rule separating a stall the office is still letting from one whose term has run out.
///
/// <para>These exist because the facility pages had no notion of expiry at all — their only flag meant "not formally
/// closed" — so a page's header counted 2 stalls being let while the grid beneath it listed 5, and three lessees whose
/// contracts had ended two months earlier appeared among the current ones with no indication of it.</para>
/// </summary>
public class TermExpiryRuleTests
{
    private static readonly DateOnly Start = new(2023, 6, 7);

    [Fact]
    public void A_term_still_running_has_not_expired()
    {
        // Three years from 7 Jun 2023, asked a year in: plainly live.
        Assert.False(DomainRules.TermHasExpired(Start, 3, new DateOnly(2024, 6, 7)));
    }

    [Fact]
    public void The_last_day_of_the_term_is_still_inside_it()
    {
        // A three-year term effective the 7th runs THROUGH the 7th three years on — the reading the office's own
        // paper takes. Expiring a day early would put a lessee in the inactive register while still under contract.
        Assert.False(DomainRules.TermHasExpired(Start, 3, new DateOnly(2026, 6, 7)));
    }

    [Fact]
    public void The_day_after_the_term_ends_has_expired()
    {
        Assert.True(DomainRules.TermHasExpired(Start, 3, new DateOnly(2026, 6, 8)));
    }

    [Fact]
    public void An_open_ended_space_never_expires()
    {
        // A space let without a contract carries a term of zero years. Written out by hand as
        // "today is past effectivity plus zero years", this was TRUE of every such space from the day after it was
        // let — so the iceplant and barbecue spaces, all let this way and all still occupied, read as vacant and had
        // their numbers offered up for reuse by the next import.
        Assert.False(DomainRules.TermHasExpired(Start, 0, new DateOnly(2026, 8, 8)));
        Assert.False(DomainRules.TermHasExpired(Start, 0, new DateOnly(2099, 1, 1)));
    }

    [Fact]
    public void A_negative_term_is_treated_as_open_ended_rather_than_expired()
    {
        // Defensive: bad data must not silently evict a sitting lessee from the current list.
        Assert.False(DomainRules.TermHasExpired(Start, -1, new DateOnly(2026, 8, 8)));
    }

    [Fact]
    public void No_contract_on_record_cannot_have_expired()
    {
        Assert.False(DomainRules.TermHasExpired((DateOnly?)null, 3, new DateOnly(2026, 8, 8)));
    }

    [Fact]
    public void The_datetime_overload_agrees_with_the_dateonly_rule_and_ignores_the_time_of_day()
    {
        // The facility pages hold DateTime and ask "as of now". A term ending today must not expire merely because
        // the clock has moved past midnight.
        var start = new DateTime(2023, 6, 7, 0, 0, 0);
        Assert.False(DomainRules.TermHasExpired(start, 3, new DateTime(2026, 6, 7, 23, 59, 0)));
        Assert.True(DomainRules.TermHasExpired(start, 3, new DateTime(2026, 6, 8, 0, 1, 0)));
        Assert.False(DomainRules.TermHasExpired((DateTime?)null, 3, new DateTime(2026, 8, 8, 12, 0, 0)));
    }
}
