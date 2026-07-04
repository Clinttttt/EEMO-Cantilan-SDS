using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;
using EEMOCantilanSDS.Domain.Entities.Payments;
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
        IReadOnlyList<UtilityBill>? utilityBills = null)
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
        stalls.Setup(s => s.GetContractAttentionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContractAttentionDto>
            {
                new(FacilityCode.ICE, "02", "Luz Mendoza", new DateOnly(2026, 5, 30), IsExpired: true),
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

        // Current-period unpaid: only the genuine one (stall 12). Stall 09 is deduped (already in arrears).
        var current = Assert.Single(items, i => i.ReasonKind == "current");
        Assert.Contains("12", current.Identifier);

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
}
