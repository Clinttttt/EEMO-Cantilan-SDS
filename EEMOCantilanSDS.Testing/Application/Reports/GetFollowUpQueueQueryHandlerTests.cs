using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>
/// The Follow-up Queue composes existing canonical sources into one action list. These tests lock in
/// the composition rules: delinquent (3+) and arrears (1–2) split into different sections; a current-
/// period unpaid stall already counted under delinquency is NOT duplicated; an excused/absent stall is
/// shown for review (₱0, never as a debt); contract expiry and online "awaiting OR" surface correctly.
/// </summary>
public class GetFollowUpQueueQueryHandlerTests
{
    private static StallComplianceDto Stall(string stallNo, string occupant, string status, decimal balance, int absentDays = 0) =>
        new(Guid.NewGuid(), stallNo, occupant, occupant, "", "", 0m, 0m,
            status, 0m, balance, null, 0, 0, null, 0, balance, absentDays);

    private static FacilityReportsDto Report(IReadOnlyList<StallComplianceDto> compliance) =>
        new(0m, 0m, 0m, 0m, compliance.Count, compliance.Count,
            compliance.Count(c => c.Balance > 0m), compliance.Sum(c => c.Balance),
            Array.Empty<RevenueTrendDto>(),
            new PaymentStatusDistributionDto(0, 0m, 0, 0m, 0, 0m),
            Array.Empty<SectionBreakdownDto>(), Array.Empty<TopStallDto>(),
            new CollectionPerformanceDto(0, 0, 0),
            DailyCollectionStreak: null, FeeTypeBreakdown: null,
            FishKiloTrend: Array.Empty<FishKiloTrendDto>(), StallCompliance: compliance);

    private static GetFollowUpQueueQueryHandler Build(
        IReadOnlyList<UnreceiptedPaymentDto>? cash = null,
        IReadOnlyList<UtilityBill>? utilityBills = null,
        IReadOnlyList<ClosedStallAccountDto>? closedAccounts = null)
    {
        var reports = new Mock<IFacilityReportsRepository>();
        var empty = Report(Array.Empty<StallComplianceDto>());

        // NPM compliance: an arrears stall (also in delinquency → must NOT duplicate as current),
        // an excused/absent stall, and a separate current-period unpaid stall.
        var npm = Report(new[]
        {
            Stall("09", "Ben Cruz", "Unpaid", 2_400m),          // same as the arrears delinquency row
            Stall("F-3", "Nida Flores", "Absent", 0m, absentDays: 30),
            Stall("12", "Lito Yu", "Unpaid", 1_500m),           // genuine current-period unpaid
        });

        reports.Setup(r => r.GetFacilityReportsAsync(
                It.IsAny<FacilityCode>(), It.IsAny<ReportPeriod>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FacilityCode code, ReportPeriod _, int _, int? _, int? _, CancellationToken _) =>
                code == FacilityCode.NPM ? npm : empty);

        reports.Setup(r => r.GetDelinquentStallsAsync(
                It.IsAny<FacilityCode?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DelinquentStallDto>
            {
                new(FacilityCode.TCC, "04", "Rosa Magbanua", 3, 12_000m),  // delinquent (3 mo)
                new(FacilityCode.NPM, "09", "Ben Cruz", 1, 2_400m),        // arrears (1 mo)
            });

        var stalls = new Mock<IStallRepository>();
        // The register of inactive accounts. The live queue reads it to surface occupancies that ended THIS period
        // and still owe — without this the queue is handed nothing and those balances appear on no screen the office
        // opens during the month it should be collecting them.
        stalls.Setup(s => s.GetClosedStallAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(closedAccounts ?? Array.Empty<ClosedStallAccountDto>());
        stalls.Setup(s => s.GetContractAttentionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContractAttentionDto>
            {
                new(Guid.NewGuid(), FacilityCode.ICE, "02", "Luz Mendoza", new DateOnly(2023, 5, 30), new DateOnly(2026, 5, 30), IsExpired: true),
            });

        var online = new Mock<IOnlinePaymentRepository>();
        online.Setup(o => o.GetAwaitingOrAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OnlinePaymentAwaitingOrDto>
            {
                new(Guid.NewGuid(), "REF-1", FacilityCode.NCC, "07", "Ana Lim", "2026-06", 3_240m, "GCash", DateTime.UtcNow),
            });

        var payments = new Mock<IPaymentRepository>();
        payments.Setup(p => p.GetUnreceiptedCashPaymentsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cash ?? Array.Empty<UnreceiptedPaymentDto>());

        var slaughter = new Mock<ISlaughterRepository>();
        slaughter.Setup(s => s.GetTransactionsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SlaughterTransactionDto>());

        var trm = new Mock<ITrmRepository>();
        trm.Setup(t => t.GetTripsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TrmTripDto>());

        var tpm = new Mock<ITpmRepository>();
        tpm.Setup(t => t.GetMonthAttendanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TpmVendorAttendanceDto>());

        var utilities = new Mock<IUtilityBillRepository>();
        utilities.Setup(u => u.GetForMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(utilityBills ?? Array.Empty<UtilityBill>());

        return new GetFollowUpQueueQueryHandler(
            reports.Object, stalls.Object, online.Object, payments.Object, slaughter.Object, trm.Object, tpm.Object, utilities.Object);
    }

    [Fact]
    public async Task Composes_DelinquentArrearsCurrentExcusedContractAndOnlineOr_WithoutDuplicates()
    {
        var handler = Build();

        var result = await handler.Handle(new GetFollowUpQueueQuery(2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items;

        // Delinquent (3+ mo) → immediate (section 1), Critical.
        var delinquent = Assert.Single(items, i => i.ReasonKind == "delinquent");
        Assert.Equal(1, delinquent.Section);
        Assert.Equal("Critical", delinquent.Priority);
        Assert.Equal("View vendor", delinquent.Action);
        Assert.Equal("/profile/tcc/04", delinquent.Link);

        // Arrears (1–2 mo) → this-period (section 2).
        var arrears = Assert.Single(items, i => i.ReasonKind == "arrears");
        Assert.Equal(2, arrears.Section);

        // Current-period unpaid is stated for BOTH stalls: the arrears figure covers months that have already
        // elapsed and excludes the month in progress, so a stall can be behind on past months and also owe this
        // one. Suppressing the row for stall 09 dropped its current-month balance off the queue entirely.
        var current = items.Where(i => i.ReasonKind == "current").ToList();
        Assert.Equal(2, current.Count);
        Assert.Contains(current, i => i.Identifier.Contains("12"));
        Assert.Contains(current, i => i.Identifier.Contains("09"));

        // Excused/absent → review (section 3), ₱0, flagged excused (never a debt).
        var excused = Assert.Single(items, i => i.ReasonKind == "excused");
        Assert.Equal(3, excused.Section);
        Assert.True(excused.Excused);
        Assert.Equal(0m, excused.Amount);

        // Expired contract is EXCLUDED from the LIVE queue — an already-expired contract is a closed
        // account (surfaced on the Closed Accounts page and via the Follow-up History handler), not a
        // live action item. Only contracts EXPIRING SOON (still active) surface here, so with the only
        // seeded contract-attention row being expired, no "contract" item appears.
        Assert.DoesNotContain(items, i => i.ReasonKind == "contract");

        // Online payment awaiting OR → Missing OR, Encode OR action.
        var missingOr = Assert.Single(items, i => i.ReasonKind == "missingor");
        Assert.Equal("Encode OR", missingOr.Action);
        Assert.Equal(3_240m, missingOr.Amount);
    }

    [Fact]
    public async Task CashPaidRecord_MissingOr_SurfacesAsImmediate_WithoutDuplicatingOnline()
    {
        // A fully-paid cash monthly record with a blank OR (TCC stall 03).
        var stallId = Guid.NewGuid();
        var handler = Build(new[]
        {
            new UnreceiptedPaymentDto(FacilityCode.TCC, "03", "Jose Cruz", 2_400m, 1, IsDaily: false, StallId: stallId),
        });

        var result = await handler.Handle(new GetFollowUpQueueQuery(2026, 6), CancellationToken.None);
        Assert.True(result.IsSuccess);

        var missing = result.Value!.Items.Where(i => i.ReasonKind == "missingor").ToList();
        // Online (NCC 07) + the new cash record (TCC 03) — both present, not merged or duplicated.
        Assert.Equal(2, missing.Count);

        var cash = Assert.Single(missing, i => i.Facility == FacilityCode.TCC);
        Assert.Equal(1, cash.Section);            // immediate action
        Assert.Equal("High", cash.Priority);
        Assert.Equal("Add OR", cash.Action);      // inline OR entry, carrying the stall to act on
        Assert.Equal(stallId, cash.StallId);
        Assert.Equal("/profile/tcc/03", cash.Link);
        Assert.Equal(2_400m, cash.Amount);

        // The online row keeps its own encode flow.
        var online = Assert.Single(missing, i => i.Facility == FacilityCode.NCC);
        Assert.Equal("Encode OR", online.Action);
    }

    [Fact]
    public async Task DailyCashReceipt_MissingOr_SurfacesOperational_WithInlineAddOr_AndStallId()
    {
        var stallId = Guid.NewGuid();
        // A stall with 15 paid daily collections in the period, all with a blank OR.
        var handler = Build(new[]
        {
            new UnreceiptedPaymentDto(FacilityCode.NPM, "1", "Pantom Dant", 450m, 15, IsDaily: true, StallId: stallId),
        });

        var result = await handler.Handle(new GetFollowUpQueueQuery(2026, 6), CancellationToken.None);
        Assert.True(result.IsSuccess);

        var daily = Assert.Single(result.Value!.Items, i => i.Reason == "Daily receipt · OR");
        Assert.Equal(4, daily.Section);             // facility-specific operational
        Assert.Equal("missingor", daily.ReasonKind);
        Assert.Equal("Add OR", daily.Action);       // inline modal, not "Open daily calendar"
        Assert.Equal(stallId, daily.StallId);       // carries the stall so the modal can act on it
        Assert.Equal(450m, daily.Amount);
        Assert.Contains("15 day", daily.Identifier);
    }

    [Fact]
    public async Task UtilityBalances_SurfaceAsSeparateMiscellaneousRows()
    {
        var stallId = Guid.NewGuid();
        var bill = UtilityBill.Create(
            stallId, 2026, 6,
            elecPreviousReading: 100m, elecCurrentReading: 110m, elecRatePerKwh: 10m,
            waterPreviousReading: 20m, waterCurrentReading: 22m, waterRatePerCubicMeter: 50m);

        bill.RecordPayment(
            elecOrNumber: null,
            waterOrNumber: null,
            collectorId: null,
            elecStatus: PaymentStatus.Partial,
            elecPartialAmount: 25m,
            waterStatus: PaymentStatus.Unpaid,
            waterPartialAmount: null);

        var handler = Build(utilityBills: new[] { bill });

        var result = await handler.Handle(new GetFollowUpQueueQuery(2026, 6), CancellationToken.None);
        Assert.True(result.IsSuccess);

        var misc = result.Value!.Items.Where(i => i.ReasonKind == "misc").ToList();
        Assert.Equal(2, misc.Count);

        var electric = Assert.Single(misc, i => i.Reason == "Electricity balance");
        Assert.Equal(75m, electric.Amount);
        Assert.Equal("Pay Bill", electric.Action);
        Assert.Equal("/npm", electric.Link);
        Assert.Equal(stallId, electric.StallId);
        Assert.Contains("Electricity", electric.Identifier);
        Assert.Contains("Partial", electric.Status);

        var water = Assert.Single(misc, i => i.Reason == "Water balance");
        Assert.Equal(100m, water.Amount);
        Assert.Contains("Unpaid", water.Status);
    }

    [Fact]
    public async Task AnAccountClosedThisPeriod_ThatStillOwes_AppearsOnTheLiveQueue()
    {
        var stallId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var today = PhilippineTime.Today;
        var closed = new ClosedStallAccountDto(
            stallId, InactiveAccountState.Closed, FacilityCode.TCC, "Tampak Commercial Center", "04",
            "Bernadette Lim", null,
            EffectivityDate: new DateOnly(today.Year, today.Month, 1),
            DurationYears: 0, MonthlyRate: 1_500m,
            ClosedOn: today, ExpiryDate: today,
            LifetimeCollected: 0m, Uncollected: 1_500m, ClosedBy: "EEMO Admin",
            OccupancyEndedOn: today, ContractId: contractId);

        var handler = Build(closedAccounts: new[] { closed });

        var dto = (await handler.Handle(new GetFollowUpQueueQuery(today.Year, today.Month), CancellationToken.None)).Value!;

        // The lessee has gone but the ₱1,500 has not been collected, and this is the month the office is working. The
        // live queue was handed no inactive accounts at all, so this balance appeared on no screen anyone opens during
        // the period it should be collected in — it surfaced only if somebody thought to open the whole-time view.
        var row = Assert.Single(dto.Items, i => i.Reason == "Closed account balance");
        Assert.Equal(1_500m, row.Amount);
        Assert.Equal("Bernadette Lim", row.Person);
        Assert.Equal(FacilityCode.TCC, row.Facility);
        Assert.Equal(stallId, row.StallId);
        // The term must travel with the row, or a payment recorded from it would settle whoever holds the stall now.
        Assert.Equal(contractId, row.ContractId);
        // A closed account's balance is final, so the row states the whole of it rather than a slice of the month.
        Assert.Contains("balance in full", row.Status);
    }

    [Fact]
    public async Task AnAccountClosedInAnEarlierPeriod_StaysOffTheLiveQueue()
    {
        var today = PhilippineTime.Today;
        var longClosed = new DateOnly(today.Year - 2, 3, 14);
        var closed = new ClosedStallAccountDto(
            Guid.NewGuid(), InactiveAccountState.Closed, FacilityCode.TCC, "Tampak Commercial Center", "05",
            "Jessie Navarro", null,
            EffectivityDate: new DateOnly(today.Year - 3, 1, 1),
            DurationYears: 0, MonthlyRate: 900m,
            ClosedOn: longClosed, ExpiryDate: longClosed,
            LifetimeCollected: 0m, Uncollected: 900m, ClosedBy: "EEMO Admin",
            OccupancyEndedOn: longClosed);

        var handler = Build(closedAccounts: new[] { closed });

        var dto = (await handler.Handle(new GetFollowUpQueueQuery(today.Year, today.Month), CancellationToken.None)).Value!;

        // Otherwise the month's work queue slowly becomes the register: every account ever closed, restated on the
        // collector's list forever. Older ended accounts are read where they belong — the Whole-time History and the
        // register of inactive accounts.
        Assert.DoesNotContain(dto.Items, i => i.Person == "Jessie Navarro");
    }
}
