using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>A Public Market its LGU rents by the month reads as a monthly-rental facility in the follow-up queue: months owed, not daily fees, and no daily coverage gap.</summary>
public class FollowUpMonthlyMarketTests
{
    private static FacilityReportsDto Report(bool billsDaily) => new(
        0m, 0m, 0m, 0m, 0, 0, 0, 0m, [], null!, [], [], null!, new DailyCollectionStreakDto("Oct 2026", 0, 3, 0, []), null, [], [], BillsDaily: billsDaily);

    private static FollowUpQueueDto Compose(bool billsDaily) =>
        FollowUpComposer.Compose(
            2026, 10, new DateOnly(2026, 10, 18),
            delinquency: [new DelinquentStallDto(FacilityCode.NPM, "A-1", "Lisa Vendor", 2, 3000m, Guid.NewGuid())],
            facilityReports: new Dictionary<FacilityCode, FacilityReportsDto> { [FacilityCode.NPM] = Report(billsDaily) },
            awaitingOr: Array.Empty<OnlinePaymentAwaitingOrDto>(),
            slaughter: Array.Empty<SlaughterTransactionDto>(),
            trips: Array.Empty<TrmTripDto>(),
            attendance: Array.Empty<TpmVendorAttendanceDto>(),
            unreceipted: Array.Empty<UnreceiptedPaymentDto>(),
            contracts: Array.Empty<ContractAttentionDto>(),
            utilityBills: Array.Empty<UtilityBill>());

    [Fact]
    public void A_monthly_market_owes_whole_months_with_no_daily_wording_or_coverage_gap()
    {
        var items = Compose(billsDaily: false).Items;

        var row = Assert.Single(items, x => x.Reason != null && x.Facility == FacilityCode.NPM);
        Assert.Equal("Unpaid · 2 months", row.Status);
        Assert.Equal("Monthly rental", row.Model);
        Assert.DoesNotContain(items, x => x.Reason == "NPM missed collection");
    }

    [Fact]
    public void A_daily_market_keeps_its_daily_wording()
    {
        var items = Compose(billsDaily: true).Items;

        var row = Assert.Single(items, x => x.Status.StartsWith("Unpaid"));
        Assert.Equal("Unpaid · daily fees across 2 months", row.Status);
        Assert.Equal("Daily stall", row.Model);
    }
}
