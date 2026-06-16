using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Facilities.GetMonthEndReport;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>
/// The month-end report stitches rental facilities (per-payor compliance from the report repo) with
/// transaction facilities (itemised month records from the SLH/TRM/TPM repos, grouped by payor). These
/// tests lock in: all eight facilities appear in enum order; rental facilities expose per-payor rows
/// while transaction ones expose grouped payors (repeats collapse into one row whose total reconciles);
/// and the grand totals reconcile to the sum of the per-facility figures.
/// </summary>
public class GetMonthEndReportQueryHandlerTests
{
    private static StallComplianceDto Payor(string stallNo, string occupant, decimal rate, string status, decimal paid, decimal balance, string? or) =>
        new(Guid.NewGuid(), stallNo, occupant, occupant, "", "", rate, 0m, status, paid, balance, or, 0, 0, null, 0);

    private static FacilityReportsDto Report(decimal collected, decimal outstanding, decimal rate, int paid, int partial, int unpaid, IReadOnlyList<StallComplianceDto> compliance) =>
        new(
            TotalRevenue: collected,
            RevenueGrowthPercentage: 0m,
            CollectionRate: rate,
            CollectionGrowthPercentage: 0m,
            OccupiedStalls: compliance.Count,
            TotalStalls: compliance.Count,
            PendingPaymentCount: compliance.Count(c => c.Balance > 0m),
            PendingPaymentAmount: outstanding,
            RevenueTrend: Array.Empty<RevenueTrendDto>(),
            PaymentDistribution: new PaymentStatusDistributionDto(paid, 0m, partial, 0m, unpaid, 0m),
            SectionBreakdown: Array.Empty<SectionBreakdownDto>(),
            TopStalls: Array.Empty<TopStallDto>(),
            CollectionPerformance: new CollectionPerformanceDto(paid, partial, unpaid),
            DailyCollectionStreak: null,
            FeeTypeBreakdown: null,
            FishKiloTrend: Array.Empty<FishKiloTrendDto>(),
            StallCompliance: compliance);

    private static SlaughterTransactionDto Slaughter(string owner, decimal amount, int day, string? or) =>
        new(Guid.NewGuid(), owner, AnimalType.Hog, null, 1, amount, amount, or, new DateOnly(2026, 6, day));

    private static (GetMonthEndReportQueryHandler handler, Mock<IFacilityReportsRepository> reportsRepo) Build()
    {
        var reportsRepo = new Mock<IFacilityReportsRepository>();
        var rental = new Dictionary<FacilityCode, FacilityReportsDto>
        {
            [FacilityCode.TCC] = Report(2400m, 600m, 80m, paid: 1, partial: 0, unpaid: 1, new[]
            {
                Payor("1", "Leah Gutierez", 2400m, "Paid", 2400m, 0m, "OR-1001"),
                Payor("9", "George Trigan", 600m, "Unpaid", 0m, 600m, null)
            })
        };
        var empty = Report(0m, 0m, 0m, 0, 0, 0, Array.Empty<StallComplianceDto>());
        reportsRepo.Setup(r => r.GetFacilityReportsAsync(
                It.IsAny<FacilityCode>(), It.IsAny<ReportPeriod>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FacilityCode code, ReportPeriod _, int _, int? _, int? _, CancellationToken _) =>
                rental.TryGetValue(code, out var rep) ? rep : empty);

        // SLH: Juan has two transactions (expandable), Maria one.
        var slaughterRepo = new Mock<ISlaughterRepository>();
        slaughterRepo.Setup(s => s.GetTransactionsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                Slaughter("Juan Cruz", 250m, 3, "OR-2001"),
                Slaughter("Juan Cruz", 250m, 12, "OR-2002"),
                Slaughter("Maria Lopez", 365m, 7, "OR-2003"),
            });

        // TRM: Diego two trips, Bonggo one.
        var trmRepo = new Mock<ITrmRepository>();
        trmRepo.Setup(t => t.GetTripsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TrmTripDto { Id = Guid.NewGuid(), TripNumber = 1, DriverName = "Diego", Route = "Cantilan-Surigao", Fee = 30m, ORNumber = "OR-3001", RecordedAt = new DateTime(2026, 6, 2) },
                new TrmTripDto { Id = Guid.NewGuid(), TripNumber = 2, DriverName = "Diego", Route = "Cantilan-Surigao", Fee = 30m, ORNumber = "OR-3002", RecordedAt = new DateTime(2026, 6, 9) },
                new TrmTripDto { Id = Guid.NewGuid(), TripNumber = 1, DriverName = "Bonggo", Route = "Tandag-Cantilan", Fee = 30m, ORNumber = "OR-3003", RecordedAt = new DateTime(2026, 6, 5) },
            });

        // TPM: one paid attendance, one unpaid (must be excluded).
        var tpmRepo = new Mock<ITpmRepository>();
        tpmRepo.Setup(t => t.GetMonthAttendanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TpmVendorAttendanceDto { Id = Guid.NewGuid(), VendorName = "Nena", Goods = "Vegetables", IsPaid = true, Fee = 100m, ORNumber = "OR-4001", MarketDate = new DateOnly(2026, 6, 6) },
                new TpmVendorAttendanceDto { Id = Guid.NewGuid(), VendorName = "Pedro", Goods = "Fish", IsPaid = false, Fee = 100m, ORNumber = null, MarketDate = new DateOnly(2026, 6, 6) },
            });

        return (new GetMonthEndReportQueryHandler(reportsRepo.Object, slaughterRepo.Object, trmRepo.Object, tpmRepo.Object), reportsRepo);
    }

    [Fact]
    public async Task ReturnsAllEightFacilities_InEnumOrder()
    {
        var (handler, _) = Build();

        var result = await handler.Handle(new GetMonthEndReportQuery(2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(8, result.Value!.Facilities.Count);
        Assert.Equal(
            new[] { FacilityCode.NPM, FacilityCode.TCC, FacilityCode.NCC, FacilityCode.BBQ, FacilityCode.ICE, FacilityCode.SLH, FacilityCode.TRM, FacilityCode.TPM },
            result.Value.Facilities.Select(f => f.Code).ToArray());
    }

    [Fact]
    public async Task RentalHasPayors_TransactionGroupsByPayor_RepeatsCollapse()
    {
        var (handler, _) = Build();

        var report = (await handler.Handle(new GetMonthEndReportQuery(2026, 6), CancellationToken.None)).Value!;

        var tcc = report.Facilities.Single(f => f.Code == FacilityCode.TCC);
        Assert.True(tcc.IsRental);
        Assert.Equal(2, tcc.Payors.Count);
        Assert.Empty(tcc.TransactionPayors);

        var slh = report.Facilities.Single(f => f.Code == FacilityCode.SLH);
        Assert.False(slh.IsRental);
        Assert.Empty(slh.Payors);
        Assert.Equal(2, slh.TransactionPayors.Count);                  // Juan + Maria
        var juan = slh.TransactionPayors.Single(p => p.Payor == "Juan Cruz");
        Assert.Equal(2, juan.RecordCount);                              // two transactions collapse into one row
        Assert.Equal(500m, juan.TotalCollected);
        Assert.Equal(2, juan.Records.Count);
        Assert.Equal("2 Hog", juan.Summary);                            // animal-type summary
        Assert.Equal(2, juan.Quantity);                                 // total heads (1 per hog)
        Assert.Equal(865m, slh.Collected);                             // reconciles to the sum of its payors

        // TRM: driver's route + trip count surface as the context columns.
        var trm = report.Facilities.Single(f => f.Code == FacilityCode.TRM);
        var diego = trm.TransactionPayors.Single(p => p.Payor == "Diego");
        Assert.Equal("Cantilan-Surigao", diego.Summary);                // route(s)
        Assert.Equal(2, diego.Quantity);                                // trips

        // TPM excludes the unpaid attendance; goods + Fridays surface as context.
        var tpm = report.Facilities.Single(f => f.Code == FacilityCode.TPM);
        var nena = Assert.Single(tpm.TransactionPayors);
        Assert.Equal("Vegetables", nena.Summary);                       // goods
        Assert.Equal(1, nena.Quantity);                                 // Fridays attended
        Assert.Equal(100m, tpm.Collected);
    }

    [Fact]
    public async Task GrandTotals_ReconcileToSumOfFacilities()
    {
        var (handler, _) = Build();

        var report = (await handler.Handle(new GetMonthEndReportQuery(2026, 6), CancellationToken.None)).Value!;

        Assert.Equal(report.Facilities.Sum(f => f.Collected), report.TotalCollected);
        Assert.Equal(report.Facilities.Sum(f => f.Outstanding), report.TotalOutstanding);
        // TCC 2400 + SLH 865 + TRM 90 + TPM 100 = 3455 collected; 600 outstanding (TCC only).
        Assert.Equal(3455m, report.TotalCollected);
        Assert.Equal(600m, report.TotalOutstanding);
        // 3455 / (3455 + 600) = 85.2% -> 85
        Assert.Equal(85, report.OverallCollectionRate);
        Assert.Equal("June 2026", report.PeriodLabel);
    }
}
