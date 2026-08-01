using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The register and the follow-up history must agree about what a lessee owes. A lessee whose occupancy ENDED — the
/// stall handed to someone else — is no longer the stall's contract holder, so nothing in the queue's contract
/// section could surface them: the register showed ₱31,980 while the history showed only the period's ₱210. Their
/// whole balance now appears as its own row.
/// </summary>
public class FollowUpEndedOccupancyTests
{
    private static ClosedStallAccountDto Account(decimal uncollected, InactiveAccountState state = InactiveAccountState.Expired) =>
        new(
            StallId: Guid.NewGuid(),
            State: state,
            FacilityCode: FacilityCode.NPM,
            FacilityName: "New Public Market",
            StallNo: "3",
            Occupant: "Ramil C. Orjeles",
            ContractName: "Ramil C. Orjeles",
            EffectivityDate: new DateOnly(2023, 6, 1),
            DurationYears: 3,
            MonthlyRate: 900m,
            ClosedOn: null,
            ExpiryDate: new DateOnly(2026, 6, 7),
            LifetimeCollected: 930m,
            Uncollected: uncollected,
            ClosedBy: "Admin",
            Section: "Fish Area",
            OccupancyEndedOn: new DateOnly(2026, 6, 30),
            StallReLet: true);

    private static FollowUpQueueDto Compose(params ClosedStallAccountDto[] accounts) =>
        FollowUpComposer.Compose(
            2026, 6, new DateOnly(2026, 6, 30),
            delinquency: Array.Empty<DelinquentStallDto>(),
            facilityReports: new Dictionary<FacilityCode, FacilityReportsDto>(),
            awaitingOr: Array.Empty<OnlinePaymentAwaitingOrDto>(),
            slaughter: Array.Empty<SlaughterTransactionDto>(),
            trips: Array.Empty<TrmTripDto>(),
            attendance: Array.Empty<TpmVendorAttendanceDto>(),
            unreceipted: Array.Empty<UnreceiptedPaymentDto>(),
            contracts: Array.Empty<ContractAttentionDto>(),
            utilityBills: Array.Empty<UtilityBill>(),
            expiredBalances: null,
            endedOccupancies: accounts);

    [Fact]
    public void APastOccupancyWithABalance_IsListedWithItsWholeBalance()
    {
        var queue = Compose(Account(31_980m));

        var row = Assert.Single(queue.Items);
        Assert.Equal("Past occupancy balance", row.Reason);
        Assert.Equal("Ramil C. Orjeles", row.Person);
        Assert.Equal("Stall 3", row.Identifier);
        Assert.Equal(31_980m, row.Amount);                  // the register's figure, not the period's ₱210
        Assert.Equal("No longer the occupant", row.Status);
        Assert.Contains("Jun 30, 2026", row.Period);        // the day the occupancy ended
    }

    [Fact]
    public void ASettledPastOccupancy_IsNotListed()
    {
        Assert.Empty(Compose(Account(0m)).Items);
    }

    [Fact]
    public void AClosedAccount_ReadsAsClosedRatherThanHandedOver()
    {
        var row = Assert.Single(Compose(Account(5_000m, InactiveAccountState.Closed)).Items);
        Assert.Equal("Closed account balance", row.Reason);
    }

    [Fact]
    public void AWholeYearView_LabelsItsRowsWithTheYear_NotTheYearsLastMonth()
    {
        // Reported by the office: with the month filter on "Whole year" every row still read "December 2025", while
        // the figure beside it was the year's. The heading and the figure have to agree.
        var queue = FollowUpComposer.Compose(
            2025, 12, new DateOnly(2025, 12, 31),
            delinquency: new[]
            {
                new DelinquentStallDto(FacilityCode.NPM, "1", "Merlita A. Abuso", 12, 10_950m, Guid.NewGuid())
            },
            facilityReports: new Dictionary<FacilityCode, FacilityReportsDto>(),
            awaitingOr: Array.Empty<OnlinePaymentAwaitingOrDto>(),
            slaughter: Array.Empty<SlaughterTransactionDto>(),
            trips: Array.Empty<TrmTripDto>(),
            attendance: Array.Empty<TpmVendorAttendanceDto>(),
            unreceipted: Array.Empty<UnreceiptedPaymentDto>(),
            contracts: Array.Empty<ContractAttentionDto>(),
            utilityBills: Array.Empty<UtilityBill>(),
            expiredBalances: null,
            endedOccupancies: null,
            periodLabelOverride: "January – December 2025");

        var row = Assert.Single(queue.Items);
        Assert.Equal("January – December 2025", row.Period);
        Assert.Equal(10_950m, row.Amount);
        Assert.Equal("January – December 2025", queue.PeriodLabel);
    }

    [Fact]
    public void TheWholeTimeView_IsLabelledByItsScope_NotByAMonth()
    {
        // The confusion this view removes: a lifetime figure (Jun 2023 → Jun 2026) shown under a single-year
        // heading. Its label must say what the figures are, so nobody reads them as one month's amount.
        var queue = FollowUpComposer.Compose(
            2026, 6, new DateOnly(2026, 6, 30),
            delinquency: Array.Empty<DelinquentStallDto>(),
            facilityReports: new Dictionary<FacilityCode, FacilityReportsDto>(),
            awaitingOr: Array.Empty<OnlinePaymentAwaitingOrDto>(),
            slaughter: Array.Empty<SlaughterTransactionDto>(),
            trips: Array.Empty<TrmTripDto>(),
            attendance: Array.Empty<TpmVendorAttendanceDto>(),
            unreceipted: Array.Empty<UnreceiptedPaymentDto>(),
            contracts: Array.Empty<ContractAttentionDto>(),
            utilityBills: Array.Empty<UtilityBill>(),
            expiredBalances: null,
            endedOccupancies: new[] { Account(31_980m) },
            periodLabelOverride: "Whole time");

        Assert.Equal("Whole time", queue.PeriodLabel);
        Assert.Equal(31_980m, Assert.Single(queue.Items).Amount);
    }
}
