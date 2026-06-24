using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>
/// The financial report composes the canonical per-facility report (stall facilities) with the
/// paid-on-service facilities (SLH/TRM/TPM). These tests lock in: totals reconcile to the facility
/// breakdown; the rate is amount-based; delinquent (3+ months) and arrears (1–2 months) are split;
/// paid-on-service facilities carry no unpaid balance and a 100% rate; recent records are mapped.
/// </summary>
public class GetFinancialReportQueryHandlerTests
{
    private static StallComplianceDto Payor(string stallNo, string occupant, decimal paid, decimal balance, int missedMonths) =>
        new(Guid.NewGuid(), stallNo, occupant, occupant, "", "", 0m, 0m,
            balance > 0 ? "Partial" : "Paid", paid, balance, null, missedMonths, 0, null, 0, paid + balance);

    private static FacilityReportsDto Report(decimal collected, decimal outstanding, decimal rate, int paid, int partial, int unpaid, IReadOnlyList<StallComplianceDto> compliance, FeeTypeBreakdownDto? feeBreakdown = null) =>
        new(
            TotalRevenue: collected,
            RevenueGrowthPercentage: 0m,
            CollectionRate: rate,
            CollectionGrowthPercentage: 0m,
            OccupiedStalls: compliance.Count,
            TotalStalls: compliance.Count,
            PendingPaymentCount: compliance.Count(c => c.Balance > 0m),
            PendingPaymentAmount: outstanding,
            RevenueTrend: new[] { new RevenueTrendDto("Mar", collected, collected + outstanding, true) },
            PaymentDistribution: new PaymentStatusDistributionDto(paid, 0m, partial, 0m, unpaid, 0m),
            SectionBreakdown: Array.Empty<SectionBreakdownDto>(),
            TopStalls: Array.Empty<TopStallDto>(),
            CollectionPerformance: new CollectionPerformanceDto(paid, partial, unpaid),
            DailyCollectionStreak: null,
            FeeTypeBreakdown: feeBreakdown,
            FishKiloTrend: Array.Empty<FishKiloTrendDto>(),
            StallCompliance: compliance);

    private static (GetFinancialReportQueryHandler handler, Mock<IFacilityReportsRepository> reports) Build()
    {
        var reports = new Mock<IFacilityReportsRepository>();
        var empty = Report(0m, 0m, 0m, 0, 0, 0, Array.Empty<StallComplianceDto>());

        // NPM: collected 80,000 / outstanding 20,000 (rate 80). Three occupied stalls:
        //   one delinquent (3 missed months), one in arrears (1 missed month), one fully paid.
        //   Fee breakdown: ₱810 daily-fee + ₱346 fish (346 kg @ ₱1/kg).
        var npm = Report(80_000m, 20_000m, 80m, paid: 6, partial: 2, unpaid: 0, new[]
        {
            Payor("12", "Rosa Magbanua", 0m, 12_000m, 3),   // delinquent
            Payor("07", "Maria Velasco", 500m, 3_000m, 1),  // arrears
            Payor("01", "Pedro Santos", 900m, 0m, 0),       // fully paid (occupied, no balance)
        }, feeBreakdown: new FeeTypeBreakdownDto(810m, 346m, null));

        reports.Setup(r => r.GetFacilityReportsAsync(
                It.IsAny<FacilityCode>(), It.IsAny<ReportPeriod>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FacilityCode code, ReportPeriod _, int _, int? _, int? _, CancellationToken _) =>
                code == FacilityCode.NPM ? npm : empty);

        // Delinquency comes from the shared rolling-window method: one delinquent (3 mo) + one arrears (1 mo).
        reports.Setup(r => r.GetDelinquentStallsAsync(
                It.IsAny<FacilityCode?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DelinquentStallDto>
            {
                new(FacilityCode.TCC, "04", "Rosa Magbanua", 3, 12_000m),
                new(FacilityCode.NPM, "22", "Maria Velasco", 1, 3_000m),
            });

        var slaughter = new Mock<ISlaughterRepository>();
        slaughter.Setup(s => s.GetTransactionsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SlaughterTransactionDto>());

        // TRM (paid on service): two trips → collected 60, 2 records.
        var trm = new Mock<ITrmRepository>();
        trm.Setup(t => t.GetTripsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TrmTripDto { Id = Guid.NewGuid(), TripNumber = 1, DriverName = "Diego", Route = "A", Fee = 30m, ORNumber = "OR-1", RecordedAt = new DateTime(2026, 3, 2) },
                new TrmTripDto { Id = Guid.NewGuid(), TripNumber = 2, DriverName = "Diego", Route = "A", Fee = 30m, ORNumber = "OR-2", RecordedAt = new DateTime(2026, 3, 9) },
            });

        var tpm = new Mock<ITpmRepository>();
        tpm.Setup(t => t.GetMonthAttendanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TpmVendorAttendanceDto>());

        var feed = new Mock<ITransactionFeedRepository>();
        feed.Setup(f => f.GetRecentTransactionsAsync(It.IsAny<FacilityCode?>(), It.IsAny<DateOnly?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TransactionFeedDto(Guid.NewGuid(), FacilityCode.NPM, "New Public Market", new DateTime(2026, 3, 25), true, "Luz Cano", "5", "Daily Fee", 930m, "OR-9", "Paid")
            });

        var handler = new GetFinancialReportQueryHandler(reports.Object, slaughter.Object, trm.Object, tpm.Object, feed.Object);
        return (handler, reports);
    }

    [Fact]
    public async Task AllFacilities_TotalsReconcile_RateIsAmountBased()
    {
        var (handler, _) = Build();

        var result = await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var r = result.Value!;

        // Collected = NPM 80,000 + TRM 60 ; unpaid = NPM 20,000 (TRM has none).
        Assert.Equal(80_060m, r.Collected);
        Assert.Equal(20_000m, r.CurrentPeriodUnpaid);
        Assert.Equal(100_060m, r.Billed);
        Assert.Equal(80, r.CollectionRatePct);                 // 80,060 / 100,060 = 79.9 -> 80
        Assert.Equal(8, r.FacilityCount);

        // Facility rows reconcile to the headline totals.
        Assert.Equal(r.Collected, r.Facilities.Sum(f => f.Collected));
        Assert.Equal(r.CurrentPeriodUnpaid, r.Facilities.Where(f => f.Unpaid.HasValue).Sum(f => f.Unpaid!.Value));
    }

    [Fact]
    public async Task SplitsDelinquentFromArrears_ByMissedMonths()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var delinquent = Assert.Single(r.Delinquent);
        Assert.Equal("Rosa Magbanua", delinquent.Name);
        Assert.Equal(3, delinquent.UnpaidMonths);
        Assert.Equal(12_000m, delinquent.Balance);

        var arrears = Assert.Single(r.Arrears);
        Assert.Equal("Maria Velasco", arrears.Name);
        Assert.Equal(1, arrears.UnpaidMonths);
    }

    [Fact]
    public async Task PaidOnServiceFacilities_HaveNoUnpaid_AndFullRate()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var trm = r.Facilities.Single(f => f.Code == FacilityCode.TRM);
        Assert.True(trm.PaidOnService);
        Assert.Null(trm.Unpaid);
        Assert.Equal(100, trm.RatePct);
        Assert.Equal(60m, trm.Collected);
        Assert.Equal(2, trm.PaidRecords);

        var npm = r.Facilities.Single(f => f.Code == FacilityCode.NPM);
        Assert.False(npm.PaidOnService);
        Assert.Equal(20_000m, npm.Unpaid);
    }

    [Fact]
    public async Task SingleFacilityScope_OnlyReturnsThatFacility()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, FacilityCode.NPM), CancellationToken.None)).Value!;

        Assert.Equal(1, r.FacilityCount);
        var only = Assert.Single(r.Facilities);
        Assert.Equal(FacilityCode.NPM, only.Code);
        Assert.Equal(80_000m, r.Collected);   // TRM excluded from scope
    }

    [Fact]
    public async Task NpmRow_HasDetailBreakdown_FishAndFullMonthCoverage()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var npm = r.Facilities.Single(f => f.Code == FacilityCode.NPM);
        var d = npm.Detail!;
        Assert.NotNull(d);
        Assert.Equal(810m, d.DailyFeeCollected);
        Assert.Equal(346m, d.FishCollected);
        Assert.Equal(346m, d.FishKilos);                 // ₱1/kg → kilos == fish amount
        Assert.Equal(20_000m, d.PeriodBalance);          // selected-period assessed − collected
        Assert.Equal(2_700m, d.FullMonthCoverage);       // 3 occupied stalls × ₱900
        Assert.Equal(1_300m, d.FullMonthCoverageBalance);// per stall: max(0,900-0)+max(0,900-500)+max(0,900-900)=1,300

        // Non-NPM facilities carry no detail.
        Assert.Null(r.Facilities.Single(f => f.Code == FacilityCode.TRM).Detail);
    }

    [Fact]
    public async Task Trend_SelectedBarMatchesKpi_AndFoldsServiceIntoPriorMonths()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        Assert.Equal(6, r.Trend.Count);                       // Monthly = last 6 months

        var selected = r.Trend.Single(p => p.IsSelected);
        Assert.Equal(r.Trend[^1], selected);                  // selected = the latest bar
        Assert.Equal(r.Collected, selected.Collected);        // reconciles to the Collected KPI (incl. service)
        Assert.Equal(r.CurrentPeriodUnpaid, selected.Unpaid);

        // Earlier months fold in the paid-on-service facilities (TRM = 2 trips × ₱30 = ₱60 in the mock).
        Assert.All(r.Trend.Where(p => !p.IsSelected), p => Assert.Equal(60m, p.Collected));

        // Month-over-month: previous-period collected = the bar immediately before the selected one.
        Assert.Equal(r.Trend[^2].Collected, r.CollectedPreviousPeriod);
        Assert.Equal(60m, r.CollectedPreviousPeriod);
    }

    [Fact]
    public async Task MapsRecentRecords_FromFeed()
    {
        var (handler, _) = Build();

        var r = (await handler.Handle(new GetFinancialReportQuery(ReportPeriod.Monthly, 2026, 3, null), CancellationToken.None)).Value!;

        var rec = Assert.Single(r.RecentRecords);
        Assert.Equal("OR-9", rec.Reference);
        Assert.Equal("Luz Cano", rec.Payor);
        Assert.Equal("Daily Fee", rec.Method);
        Assert.Equal(930m, rec.Amount);
    }
}
