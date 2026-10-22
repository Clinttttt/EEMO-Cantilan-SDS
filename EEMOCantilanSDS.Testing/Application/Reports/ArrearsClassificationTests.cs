using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;
using EEMOCantilanSDS.Domain.Common.Billing;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// IA-019 / IA-020 / IA-069: every unpaid rental obligation is sorted on its own facts. Previous-calendar-year rent and the rent of ended
/// occupancies or expired contracts are Arrears; fully elapsed current-year months of a live account are Delinquent; the month in progress
/// is never delinquent. One account can carry several categories, and no peso is in two of them.
/// </summary>
public class ArrearsClassificationTests
{
    // ── The rule, obligation by obligation ───────────────────────────────────────────────────────────

    [Theory]
    // The year boundary: unpaid December 2025 is Arrears for the whole of 2026, even for a tenant who stays.
    [InlineData(2025, 12, "2026-01-02", false, ReceivableCategory.Arrears)]
    [InlineData(2025, 1, "2026-10-19", false, ReceivableCategory.Arrears)]
    // The same December is still just an elapsed current-year month while 2025 is the year being read.
    [InlineData(2025, 12, "2025-12-31", false, ReceivableCategory.CurrentPeriod)]
    [InlineData(2025, 11, "2025-12-15", false, ReceivableCategory.Delinquent)]
    // Current year, fully elapsed => Delinquent; the month in progress => current-period, never delinquent.
    [InlineData(2026, 9, "2026-10-19", false, ReceivableCategory.Delinquent)]
    [InlineData(2026, 1, "2026-10-19", false, ReceivableCategory.Delinquent)]
    [InlineData(2026, 10, "2026-10-19", false, ReceivableCategory.CurrentPeriod)]
    [InlineData(2026, 10, "2026-10-31", false, ReceivableCategory.CurrentPeriod)]   // the last day of the month is still in progress
    [InlineData(2026, 10, "2026-11-01", false, ReceivableCategory.Delinquent)]      // the first day after it closes
    [InlineData(2026, 12, "2026-10-19", false, ReceivableCategory.CurrentPeriod)]   // not yet due
    // An ended occupancy or expired contract is Arrears whatever the year or the month.
    [InlineData(2026, 9, "2026-10-19", true, ReceivableCategory.Arrears)]
    [InlineData(2026, 10, "2026-10-19", true, ReceivableCategory.Arrears)]
    [InlineData(2023, 3, "2026-10-19", true, ReceivableCategory.Arrears)]
    public void An_obligation_is_classified_by_its_own_billing_month_and_whether_its_occupancy_ended(
        int year, int month, string reference, bool ended, ReceivableCategory expected) =>
        Assert.Equal(expected, ReceivableClassification.Classify(year, month, DateOnly.Parse(reference), ended));

    // ── One account, several categories, nothing counted twice ───────────────────────────────────────

    private static FollowUpQueueDto Compose(params DelinquentStallDto[] accounts) =>
        FollowUpComposer.Compose(
            2026, 10, new DateOnly(2026, 10, 19),
            delinquency: accounts,
            facilityReports: new Dictionary<FacilityCode, FacilityReportsDto>(),
            awaitingOr: Array.Empty<OnlinePaymentAwaitingOrDto>(),
            slaughter: Array.Empty<SlaughterTransactionDto>(),
            trips: Array.Empty<TrmTripDto>(),
            attendance: Array.Empty<TpmVendorAttendanceDto>(),
            unreceipted: Array.Empty<UnreceiptedPaymentDto>(),
            contracts: Array.Empty<ContractAttentionDto>(),
            utilityBills: Array.Empty<UtilityBill>());

    private static DelinquentStallDto Account(int arrearsMonths, decimal arrears, int delinquentMonths, decimal delinquent, bool lapsed = false) =>
        new(FacilityCode.TCC, "07", "Rosa Magbanua", arrearsMonths + delinquentMonths, arrears + delinquent, Guid.NewGuid(), lapsed, "",
            Classified: true, ArrearsMonths: arrearsMonths, ArrearsAmount: arrears, DelinquentMonths: delinquentMonths, DelinquentAmount: delinquent,
            ArrearsFirstPeriod: arrearsMonths > 0 ? "2025-11" : null, ArrearsLastPeriod: arrearsMonths > 0 ? "2025-12" : null,
            DelinquentFirstPeriod: delinquentMonths > 0 ? "2026-01" : null, DelinquentLastPeriod: delinquentMonths > 0 ? "2026-03" : null);

    [Fact]
    public void An_account_with_previous_year_and_current_year_months_is_two_rows_whose_money_adds_up_to_the_account()
    {
        // Two unpaid months of last year and three of this year, ₱1,500 each.
        var account = Account(2, 3_000m, 3, 4_500m);

        var items = Compose(account).Items;

        var arrears = Assert.Single(items, i => i.ReasonKind == "arrears");
        var delinquent = Assert.Single(items, i => i.ReasonKind == "delinquent");
        Assert.Equal(3_000m, arrears.Amount);
        Assert.Equal(4_500m, delinquent.Amount);
        Assert.Equal(account.OutstandingBalance, arrears.Amount + delinquent.Amount);     // nothing counted twice, nothing dropped
        Assert.Contains("previous year", arrears.Status);
        Assert.Equal("Nov 2025 – Dec 2025", arrears.Period);
        Assert.Equal("Jan 2026 – Mar 2026", delinquent.Period);
        Assert.Equal(account.StallId, arrears.StallId);                                     // the same account, so it can be opened and paid from either row
        Assert.Equal(account.StallId, delinquent.StallId);
    }

    [Theory]
    [InlineData(1, "Normal", 2)]     // IA-019: one or two elapsed months are Normal / this period
    [InlineData(2, "Normal", 2)]
    [InlineData(3, "Critical", 1)]   // three or more are Critical / immediate
    [InlineData(5, "Critical", 1)]
    public void Delinquent_severity_follows_the_current_year_months_and_is_not_changed_by_arrears(int delinquentMonths, string priority, int section)
    {
        // The same account also owes a lot of Arrears; that must not raise (or lower) the delinquent severity.
        var items = Compose(Account(10, 15_000m, delinquentMonths, 1_500m * delinquentMonths)).Items;

        var delinquent = Assert.Single(items, i => i.ReasonKind == "delinquent");
        Assert.Equal((priority, section), (delinquent.Priority, delinquent.Section));
    }

    [Fact]
    public void An_account_with_only_arrears_is_not_listed_as_delinquent_and_an_expired_contract_says_so()
    {
        var items = Compose(Account(6, 9_000m, 0, 0m, lapsed: true)).Items;

        var row = Assert.Single(items);
        Assert.Equal("arrears", row.ReasonKind);
        Assert.Equal("Arrears", row.Reason);
        Assert.Contains("contract expired", row.Status);
        Assert.Equal(9_000m, row.Amount);
    }

    [Fact]
    public void A_current_month_only_account_is_neither_delinquent_nor_arrears()
    {
        Assert.Empty(Compose(Account(0, 0m, 0, 0m)).Items);
    }

    [Fact]
    public void A_producer_that_does_not_classify_behaves_as_before_all_age_is_delinquent()
    {
        var items = Compose(new DelinquentStallDto(FacilityCode.TCC, "04", "Rosa", 4, 6_000m)).Items;

        var row = Assert.Single(items);
        Assert.Equal(("delinquent", 6_000m, "Critical"), (row.ReasonKind, row.Amount, row.Priority));
    }
}
