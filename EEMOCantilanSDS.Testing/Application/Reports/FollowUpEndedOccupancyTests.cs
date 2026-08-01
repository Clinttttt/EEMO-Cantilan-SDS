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
        // The figure is the account's own, not the viewed period's, and the row says so — otherwise ₱31,980 beside a
        // year heading reads as that year's assessment.
        Assert.Equal("No longer the occupant · balance in full", row.Status);
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
        var wholeTime = Assert.Single(queue.Items);
        Assert.Equal(31_980m, wholeTime.Amount);
        // Here the figure IS the lifetime one and the whole span states it, so the row says so.
        Assert.Equal("No longer the occupant · balance in full", wholeTime.Status);
        Assert.Equal("Jun 2023 → Jun 30, 2026", wholeTime.Period);
    }

    private static ContractAttentionDto LapsedTerm() =>
        new(Guid.NewGuid(), FacilityCode.NPM, "3", "Ramil C. Orjeles",
            new DateOnly(2023, 6, 1), new DateOnly(2026, 6, 7), IsExpired: true);

    private static FollowUpQueueDto ComposeExpired(string? label, DateOnly? from, DateOnly? to) =>
        FollowUpComposer.Compose(
            2026, 7, new DateOnly(2026, 7, 31),
            delinquency: Array.Empty<DelinquentStallDto>(),
            facilityReports: new Dictionary<FacilityCode, FacilityReportsDto>(),
            awaitingOr: Array.Empty<OnlinePaymentAwaitingOrDto>(),
            slaughter: Array.Empty<SlaughterTransactionDto>(),
            trips: Array.Empty<TrmTripDto>(),
            attendance: Array.Empty<TpmVendorAttendanceDto>(),
            unreceipted: Array.Empty<UnreceiptedPaymentDto>(),
            contracts: new[] { LapsedTerm() },
            utilityBills: Array.Empty<UtilityBill>(),
            expiredBalances: null,
            endedOccupancies: null,
            periodLabelOverride: label,
            periodStart: from,
            periodEnd: to);

    [Fact]
    public void AnExpiredRow_InAYearView_StatesTheYearsOwnPartOfTheTerm()
    {
        // Reported by the office: filtered to 2026, a "Contract expired" row read "Jun 2023 → Jun 7, 2026" — the
        // whole term — while the amount beside it was 2026's assessment. The row must state the part of the term
        // that falls in the year on screen; the whole term belongs to "Whole time".
        var row = Assert.Single(ComposeExpired(
            "January – December 2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 31)).Items);

        Assert.Equal("Contract expired", row.Reason);
        Assert.Equal("Jan 1, 2026 → Jun 7, 2026", row.Period);
        Assert.DoesNotContain("2023", row.Period);
        // Nothing is lost: the day the term lapsed — the reason the row is here — is in the status line.
        Assert.Equal("Expired Jun 7, 2026 · active occupant", row.Status);
    }

    [Fact]
    public void AnExpiredRow_InAnEarlierYearsView_StopsAtThatYearsEnd()
    {
        // The same term seen from 2023: it began in June and the year ended while it still ran.
        var row = Assert.Single(ComposeExpired(
            "January – December 2023", new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31)).Items);

        Assert.Equal("Jun 2023 → Dec 31, 2023", row.Period);
    }

    [Fact]
    public void AnExpiredRow_InAMonthView_StatesThatMonthsOwnDays()
    {
        var row = Assert.Single(ComposeExpired(
            null, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)).Items);

        Assert.Equal("Jun 1, 2026 → Jun 7, 2026", row.Period);
        Assert.Equal("Expired Jun 7, 2026 · active occupant", row.Status);
    }

    [Fact]
    public void AnExpiredRow_InTheWholeTimeView_KeepsTheWholeTerm()
    {
        // The cumulative view answers "what is owed in total", so the term's whole span is the right reading there.
        var row = Assert.Single(ComposeExpired("Whole time", null, null).Items);

        Assert.Equal("Jun 2023 → Jun 7, 2026", row.Period);
        Assert.Equal("Active occupant", row.Status);
    }

    [Fact]
    public void ATermThatLapsedBeforeTheViewedPeriod_IsStatedWhole()
    {
        // A term that ran out before the year on screen has no part inside it. Clipping it would leave a single
        // meaningless day, so the row states the term as it was — which is also when the office can see it lapsed.
        var lapsedEarlier = new ContractAttentionDto(
            Guid.NewGuid(), FacilityCode.NPM, "3", "Ramil C. Orjeles",
            new DateOnly(2019, 6, 1), new DateOnly(2022, 6, 7), IsExpired: true);

        var queue = FollowUpComposer.Compose(
            2026, 7, new DateOnly(2026, 7, 31),
            delinquency: Array.Empty<DelinquentStallDto>(),
            facilityReports: new Dictionary<FacilityCode, FacilityReportsDto>(),
            awaitingOr: Array.Empty<OnlinePaymentAwaitingOrDto>(),
            slaughter: Array.Empty<SlaughterTransactionDto>(),
            trips: Array.Empty<TrmTripDto>(),
            attendance: Array.Empty<TpmVendorAttendanceDto>(),
            unreceipted: Array.Empty<UnreceiptedPaymentDto>(),
            contracts: new[] { lapsedEarlier },
            utilityBills: Array.Empty<UtilityBill>(),
            expiredBalances: null,
            endedOccupancies: null,
            periodStart: new DateOnly(2026, 1, 1),
            periodEnd: new DateOnly(2026, 7, 31));

        Assert.Equal("Jun 2019 → Jun 7, 2022", Assert.Single(queue.Items).Period);
    }

    [Fact]
    public void AnEndedOccupancy_InAYearView_StatesTheYearsOwnPartOfTheOccupancy()
    {
        // The register hands this view the year's own figure (GetClosedStallAccountsForPeriodAsync), so the span
        // beside it must be the year's part of the occupancy too.
        var queue = FollowUpComposer.Compose(
            2026, 7, new DateOnly(2026, 7, 31),
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
            endedOccupancies: new[] { Account(4_200m) },
            periodLabelOverride: "January – December 2026",
            periodStart: new DateOnly(2026, 1, 1),
            periodEnd: new DateOnly(2026, 7, 31));

        var row = Assert.Single(queue.Items);
        Assert.Equal("Jan 1, 2026 → Jun 30, 2026", row.Period);
        Assert.Equal(4_200m, row.Amount);
        // The figure is the year's, so the row must NOT call it the balance in full.
        Assert.Equal("No longer the occupant", row.Status);
    }

    [Fact]
    public void AnExpiringRow_IsUnchanged_ItStillStatesItsExpiryDate()
    {
        // The live queue shows only contracts EXPIRING SOON; this fix must not touch them.
        var expiring = new ContractAttentionDto(
            Guid.NewGuid(), FacilityCode.NPM, "3", "Ramil C. Orjeles",
            new DateOnly(2023, 6, 1), new DateOnly(2026, 9, 30), IsExpired: false);

        var queue = FollowUpComposer.Compose(
            2026, 7, new DateOnly(2026, 7, 31),
            delinquency: Array.Empty<DelinquentStallDto>(),
            facilityReports: new Dictionary<FacilityCode, FacilityReportsDto>(),
            awaitingOr: Array.Empty<OnlinePaymentAwaitingOrDto>(),
            slaughter: Array.Empty<SlaughterTransactionDto>(),
            trips: Array.Empty<TrmTripDto>(),
            attendance: Array.Empty<TpmVendorAttendanceDto>(),
            unreceipted: Array.Empty<UnreceiptedPaymentDto>(),
            contracts: new[] { expiring },
            utilityBills: Array.Empty<UtilityBill>());

        var row = Assert.Single(queue.Items);
        Assert.Equal("Contract expiring", row.Reason);
        Assert.Equal("Sep 30, 2026", row.Period);
        Assert.Equal("Expiring soon", row.Status);
    }
}
