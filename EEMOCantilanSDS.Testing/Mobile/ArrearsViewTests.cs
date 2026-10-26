using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Mobile.Presentation;

namespace EEMOCantilanSDS.UnitTest.Mobile;

public class ArrearsViewTests
{
    private static ArrearsAccountDto Account(string payer, string space, string op, string opName, decimal arrears, decimal delinquent,
        ArrearsAccountKind kind = ArrearsAccountKind.StallRent, string? first = "2026-04", string? last = "2026-09", bool canCollect = true) =>
        new($"k:{payer}", kind, new(kind == ArrearsAccountKind.StallRent ? SourceIdentityKind.Occupancy : SourceIdentityKind.SpaceAccount, Guid.NewGuid()),
            op, opName, payer, space, null, "Active", arrears > 0 && delinquent > 0 ? "both" : arrears > 0 ? "arrears" : "delinquent",
            arrears, delinquent, arrears + delinquent, 6, first, last, 0m, arrears + delinquent, null, canCollect, null, null);

    private static readonly ArrearsAccountDto[] Sample =
    [
        Account("Anna Cruz", "Stall 11", "FACILITY_TCC", "Commercial Center", 15_000m, 0m),
        Account("Ben Lopez", "Stall 11", "FACILITY_TCC", "Commercial Center", 0m, 6_000m),
        Account("Robin Padilla", "Stall 2", "FACILITY_RST", "Rest Rooms", 36_000m, 0m),                       // a custom stall-rent facility
        Account("Space Holder", "A-4", "KANMANGGAY_SPACE_RENTAL", "Kanmanggay", 1_000m, 2_000m, ArrearsAccountKind.SpaceRental),
        Account("Daily Vendor", "Stall 5", "FACILITY_NPM", "Public Market", 0m, 900m, canCollect: false),
    ];

    [Fact]
    public void Search_matches_payer_space_and_operation_without_changing_any_amount()
    {
        Assert.Equal(["Anna Cruz", "Ben Lopez"], ArrearsView.Apply(Sample, new(Search: "stall 11")).Select(a => a.Payer));
        Assert.Equal(["Robin Padilla"], ArrearsView.Apply(Sample, new(Search: "rest rooms")).Select(a => a.Payer));
        Assert.Equal(36_000m, ArrearsView.Apply(Sample, new(Search: "padilla")).Single().Total);
    }

    [Fact]
    public void Reason_filter_keeps_an_account_that_owes_both_under_each_reason()
    {
        Assert.Equal(["Anna Cruz", "Robin Padilla", "Space Holder"], ArrearsView.Apply(Sample, new(Reason: ArrearsView.ArrearsOnly)).Select(a => a.Payer));
        Assert.Equal(["Ben Lopez", "Space Holder", "Daily Vendor"], ArrearsView.Apply(Sample, new(Reason: ArrearsView.DelinquentOnly)).Select(a => a.Payer));
    }

    [Fact]
    public void Operations_list_each_configured_operation_once_standard_or_custom()
    {
        Assert.Equal(["Commercial Center", "Kanmanggay", "Public Market", "Rest Rooms"], ArrearsView.Operations(Sample).Select(o => o.Name));
        Assert.Equal(["Robin Padilla"], ArrearsView.Apply(Sample, new(OperationCode: "FACILITY_RST")).Select(a => a.Payer));
    }

    [Fact]
    public void Totals_state_exactly_the_rows_shown()
    {
        var shown = ArrearsView.Apply(Sample, new(OperationCode: "FACILITY_TCC"));
        Assert.Equal((2, 21_000m, 15_000m, 6_000m), ArrearsView.Totals(shown));
        Assert.Equal((0, 0m, 0m, 0m), ArrearsView.Totals(ArrearsView.Apply(Sample, new(Search: "nobody"))));
    }

    [Fact]
    public void Span_words_a_range_a_single_month_or_nothing()
    {
        Assert.Equal("Apr 2026 to Sep 2026", ArrearsView.Span(Sample[0]));
        Assert.Equal("Apr 2026", ArrearsView.Span(Account("x", "s", "o", "O", 1m, 0m, first: "2026-04", last: "2026-04")));
        Assert.Null(ArrearsView.Span(Account("x", "s", "o", "O", 1m, 0m, first: null, last: null)));
    }

    [Fact]
    public void A_daily_billed_account_says_why_it_cannot_be_collected_here()
    {
        Assert.Null(ArrearsView.CollectBlockedReason(Sample[0]));
        Assert.Contains("billed daily", ArrearsView.CollectBlockedReason(Sample[4]));
    }

    [Fact]
    public void A_monthly_facility_without_canonical_rent_is_routed_to_its_own_monthly_collection_not_to_a_dead_end()
    {
        var legacyMonthly = Sample[0] with { CanCollect = false, CollectVia = "FacilityMonthly", OperationCode = "FACILITY_BBQ" };
        Assert.Equal("BBQ", ArrearsView.FacilityMonthlyRoute(legacyMonthly));
        Assert.Contains("monthly collection", ArrearsView.CollectBlockedReason(legacyMonthly));
        Assert.DoesNotContain("billed daily", ArrearsView.CollectBlockedReason(legacyMonthly));

        Assert.Null(ArrearsView.FacilityMonthlyRoute(Sample[0]));                                   // collectible here: no detour
        Assert.Null(ArrearsView.FacilityMonthlyRoute(Sample[4] with { CollectVia = "Daily" }));      // daily-billed: nothing to open
        Assert.Contains("billed daily", ArrearsView.CollectBlockedReason(Sample[4] with { CollectVia = "Daily" }));
    }
}
