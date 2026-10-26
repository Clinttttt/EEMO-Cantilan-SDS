using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Client.Services;

namespace EEMOCantilanSDS.ComponentTests.Services;

/// <summary>
/// The per-account grouping of the server's per-period space feed: one row per ACCOUNT id, every period counted once, the Delinquent/Arrears and
/// current/earlier-term parts summed apart, and the whole always equal to the sum of the account's own period rows.
/// </summary>
public sealed class SpaceFollowUpAccountTests
{
    private static ObligationFollowUpItemDto Month(Guid account, string payer, DateOnly period, decimal assessed, decimal collected, string reason,
        string? basis = null, bool earlier = false, string operation = "VEGETABLE_FRUIT_SPACE_RENTAL", string display = "Vegetable / Fruits", string space = "4") =>
        new(account, Guid.Empty, payer, space, new(FollowUpScopeKind.MonthlySpace, operation, display), period, assessed, collected, assessed - collected,
            "Normal", null, "/operations/vegetable-fruit", reason, basis, earlier, new DateOnly(2025, 10, 24));

    private static IReadOnlyList<ObligationFollowUpItemDto> Greggor(Guid id) =>
        Enumerable.Range(0, 48).Select(i => Month(id, "Greggor Dal", new DateOnly(2022, 10, 1).AddMonths(i), 10_000m, 0m, "arrears", "Expired contract")).ToList();

    [Fact]
    public void FortyEightMonthsOfOneAccount_AreOneRow_StatingTheWholeOverdueBalanceAndItsSpan()
    {
        var id = Guid.NewGuid();
        var months = Greggor(id);

        foreach (var rows in new[] { SpaceFollowUpAccount.For(months, "all"), SpaceFollowUpAccount.For(months, "arrears") })
        {
            var row = Assert.Single(rows);
            Assert.Equal((id, 48, 480_000m), (row.AccountId, row.Months, row.Outstanding));
            Assert.Equal("Oct 2022 – Sep 2026", row.PeriodText);
            Assert.Equal("Arrears · term ended Oct 24, 2025", row.ReasonText);
            Assert.Equal("Unpaid", row.PaymentText);
        }
        Assert.Empty(SpaceFollowUpAccount.For(months, "delinquent"));
    }

    [Fact]
    public void AnAccountWithBothReasons_IsOneRowUnderAll_AndEachReasonFilterStatesOnlyItsOwnPart()
    {
        var id = Guid.NewGuid();
        var months = new[]
        {
            Month(id, "Ana Cruz", new(2025, 11, 1), 500m, 0m, "arrears", "Previous year"),
            Month(id, "Ana Cruz", new(2025, 12, 1), 500m, 0m, "arrears", "Previous year"),
            Month(id, "Ana Cruz", new(2026, 8, 1), 500m, 200m, "delinquent")
        };

        var all = Assert.Single(SpaceFollowUpAccount.For(months, "all"));
        Assert.Equal(("mixed", 1_000m, 300m, 1_300m, 3), (all.ReasonKind, all.ArrearsOutstanding, all.DelinquentOutstanding, all.Outstanding, all.Months));
        Assert.Equal("Arrears · previous year ₱1,000 · Delinquent ₱300", all.ReasonText);
        var arrears = Assert.Single(SpaceFollowUpAccount.For(months, "arrears"));
        Assert.Equal((1_000m, 2, 0m), (arrears.Outstanding, arrears.Months, arrears.Collected));
        var delinquent = Assert.Single(SpaceFollowUpAccount.For(months, "delinquent"));
        Assert.Equal((300m, 1, 200m, "Partial · ₱200 paid"), (delinquent.Outstanding, delinquent.Months, delinquent.Collected, delinquent.PaymentText));
        Assert.Equal(all.Outstanding, arrears.Outstanding + delinquent.Outstanding);                 // the parts add back to the whole, nothing twice
        Assert.Equal(months.Sum(m => m.Outstanding), all.Outstanding);
    }

    [Fact]
    public void ARenewedAccount_StatesItsEarlierTermApart_FromItsCurrentTerm()
    {
        var id = Guid.NewGuid();
        var months = new[]
        {
            Month(id, "Lorenofs Brando", new(2025, 9, 1), 1_000m, 0m, "arrears", "Expired contract", earlier: true, operation: "KANMANGGAY_SPACE_RENTAL", display: "Kanmanggay", space: "5"),
            Month(id, "Lorenofs Brando", new(2025, 10, 1), 1_000m, 400m, "arrears", "Expired contract", earlier: true, operation: "KANMANGGAY_SPACE_RENTAL", display: "Kanmanggay", space: "5"),
            Month(id, "Lorenofs Brando", new(2026, 3, 1), 1_000m, 0m, "delinquent", operation: "KANMANGGAY_SPACE_RENTAL", display: "Kanmanggay", space: "5")
        };

        var row = Assert.Single(SpaceFollowUpAccount.For(months, "all"));
        Assert.Equal((2, 1_600m, 1, 1_000m), (row.EarlierMonths, row.EarlierOutstanding, row.CurrentMonths, row.CurrentOutstanding));
        Assert.Equal(2_600m, row.Outstanding);
        Assert.Equal("Arrears · expired contract ₱1,600 · Delinquent ₱1,000", row.ReasonText);   // an earlier term is not "term ended" on the current one
    }

    [Fact]
    public void TwoAccountsOfOnePayor_StayTwoRows_NeverMergedByName()
    {
        var months = new[]
        {
            Month(Guid.NewGuid(), "Ana Cruz", new(2026, 7, 1), 500m, 0m, "delinquent", space: "4"),
            Month(Guid.NewGuid(), "Ana Cruz", new(2026, 7, 1), 800m, 0m, "delinquent", operation: "KANMANGGAY_SPACE_RENTAL", display: "Kanmanggay", space: "9")
        };

        var rows = SpaceFollowUpAccount.For(months, "all");
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.AccountId).Distinct().Count());
        Assert.Equal([500m, 800m], rows.Select(r => r.Outstanding).Order().ToArray());
    }

    [Fact]
    public void SearchMatchesThePayorTheSpaceOrTheOperation()
    {
        var row = Assert.Single(SpaceFollowUpAccount.For(Greggor(Guid.NewGuid()), "all"));
        Assert.True(row.Matches("greggor"));
        Assert.True(row.Matches("Vegetable"));
        Assert.True(row.Matches("4"));
        Assert.False(row.Matches("Lorenofs"));
    }
}
