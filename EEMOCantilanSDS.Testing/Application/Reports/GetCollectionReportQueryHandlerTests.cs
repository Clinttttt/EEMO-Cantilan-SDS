using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetCollectionReport;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>
/// The Export-Data collection report composes per-facility stall compliance (rental facilities) with the
/// service-facility month records. These tests lock the structured mapping: NPM rows carry their market
/// section and absent-adjusted full-month coverage; SLH/TRM/TPM rows expose their context columns; and
/// only paid TPM attendances are included.
/// </summary>
public class GetCollectionReportQueryHandlerTests
{
    private static StallComplianceDto Stall(string stallNo, string section, string occupant, string status,
        decimal dailyRate, decimal monthlyRate, decimal paid, decimal balance, int absentDays) =>
        new(Guid.NewGuid(), stallNo, occupant, occupant, section, "", monthlyRate, dailyRate,
            status, paid, balance, "OR-X", 0, 0, null, 0, balance, absentDays);

    private static FacilityReportsDto Report(decimal collected, decimal outstanding, IReadOnlyList<StallComplianceDto> compliance) =>
        new(collected, 0m, 0m, 0m, compliance.Count, compliance.Count,
            compliance.Count(c => c.Balance > 0m), outstanding,
            Array.Empty<RevenueTrendDto>(), new PaymentStatusDistributionDto(0, 0m, 0, 0m, 0, 0m),
            Array.Empty<SectionBreakdownDto>(), Array.Empty<TopStallDto>(),
            new CollectionPerformanceDto(0, 0, 0), null, null,
            Array.Empty<FishKiloTrendDto>(), compliance);

    private static GetCollectionReportQueryHandler Build()
    {
        var reports = new Mock<IFacilityReportsRepository>();
        var empty = Report(0m, 0m, Array.Empty<StallComplianceDto>());
        // NPM: one Fish Area stall, 5 excused days → coverage 900 − 5×30 = 750.
        var npmStall = Stall("1", "Fish Area", "Ana Reyes", "Paid", dailyRate: 30m, monthlyRate: 900m, paid: 750m, balance: 0m, absentDays: 5);
        var npm = Report(750m, 0m, new[] { npmStall });
        reports.Setup(r => r.GetFacilityReportsAsync(
                It.IsAny<FacilityCode>(), It.IsAny<ReportPeriod>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FacilityCode code, ReportPeriod _, int _, int? _, int? _, CancellationToken _) =>
                code == FacilityCode.NPM ? npm : empty);
        // Per-stall fish kilos: this stall sold 40 kg → ₱40 fish fee (₱1/kg).
        reports.Setup(r => r.GetNpmFishKilosByStallAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal> { [npmStall.StallId] = 40m });

        var slaughter = new Mock<ISlaughterRepository>();
        slaughter.Setup(s => s.GetTransactionsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SlaughterTransactionDto(Guid.NewGuid(), "Juan dela Cruz", AnimalType.Hog, null, 2, 250m, 500m, "OR-6001", new DateOnly(2026, 6, 3)),
            });

        var trm = new Mock<ITrmRepository>();
        trm.Setup(t => t.GetTripsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TrmTripDto { Id = Guid.NewGuid(), TripNumber = 1, DriverName = "Diego", Route = "Cantilan–Carrascal", Fee = 30m, ORNumber = "OR-7001", RecordedAt = new DateTime(2026, 6, 25) },
            });

        var tpm = new Mock<ITpmRepository>();
        tpm.Setup(t => t.GetMonthAttendanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new TpmVendorAttendanceDto { Id = Guid.NewGuid(), VendorName = "Lita Bani", Goods = "Vegetables", IsPaid = true, ORNumber = "OR-8001", Fee = 100m, MarketDate = new DateOnly(2026, 6, 12) },
                new TpmVendorAttendanceDto { Id = Guid.NewGuid(), VendorName = "Skip Me", Goods = "Fish", IsPaid = false, ORNumber = null, Fee = 100m, MarketDate = new DateOnly(2026, 6, 12) },
            });

        return new GetCollectionReportQueryHandler(reports.Object, slaughter.Object, trm.Object, tpm.Object, CacheTestDoubles.FeeRateResolver);
    }

    [Fact]
    public async Task Composes_StructuredRows_PerFacilityContext()
    {
        var handler = Build();

        var result = await handler.Handle(new GetCollectionReportQuery(2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;

        // NPM rental row: section preserved + absent-adjusted coverage (900 − 5×30 = 750).
        var npm = Assert.Single(dto.Facilities, f => f.Code == FacilityCode.NPM);
        var row = Assert.Single(npm.Rentals);
        Assert.Equal("Fish Area", row.Section);
        Assert.Equal(30m, row.Rate);
        Assert.Equal(750m, row.Coverage);
        Assert.Equal(0m, row.CoverageBalance);   // max(0, 750 − 750)
        // Fish kilos surface as a separate extra charge: 40 kg × ₱1/kg = ₱40.
        Assert.Equal(40m, row.FishKilos);
        Assert.Equal(40m, row.FishFee);

        // SLH transaction row exposes animal / heads / rate as columns.
        var slh = Assert.Single(dto.Facilities, f => f.Code == FacilityCode.SLH);
        var s = Assert.Single(slh.Transactions);
        Assert.Equal("Hog", s.Detail);
        Assert.Equal(2, s.Heads);
        Assert.Equal(250m, s.Rate);
        Assert.Equal(500m, s.Amount);

        // TRM trip row carries the trip reference + route.
        var trm = Assert.Single(dto.Facilities, f => f.Code == FacilityCode.TRM);
        var t = Assert.Single(trm.Transactions);
        Assert.Equal("Trip #1", t.Ref);
        Assert.Equal("Cantilan–Carrascal", t.Detail);

        // TPM includes paid attendance only (the unpaid one is excluded).
        var tpm = Assert.Single(dto.Facilities, f => f.Code == FacilityCode.TPM);
        var p = Assert.Single(tpm.Transactions);
        Assert.Equal("Lita Bani", p.Payor);
        Assert.Equal(100m, p.Amount);
    }
}
