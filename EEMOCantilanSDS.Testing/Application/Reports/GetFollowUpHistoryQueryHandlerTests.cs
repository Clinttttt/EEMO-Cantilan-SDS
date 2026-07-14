using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpHistory;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>
/// Follow-up History is a PAST-period snapshot: it reuses the live queue's composer but pulls the
/// contract-attention and online-awaiting-OR sources through their PERIOD-SCOPED repository methods, and
/// stamps the snapshot date as the last day of the requested period. These tests lock in that wiring —
/// the composition rules themselves are covered by GetFollowUpQueueQueryHandlerTests.
/// </summary>
public class GetFollowUpHistoryQueryHandlerTests
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

    private static (GetFollowUpHistoryQueryHandler Handler, Mock<IStallRepository> Stalls, Mock<IOnlinePaymentRepository> Online, Mock<IPaymentRepository> Payments, Mock<ISlaughterRepository> Slaughter) Build()
    {
        var reports = new Mock<IFacilityReportsRepository>();
        var empty = Report(Array.Empty<StallComplianceDto>());
        var npm = Report(new[]
        {
            Stall("09", "Ben Cruz", "Unpaid", 2_400m),   // also the arrears delinquency row → deduped
            Stall("F-3", "Nida Flores", "Absent", 0m, absentDays: 30),
            Stall("12", "Lito Yu", "Unpaid", 1_500m),    // genuine current-period unpaid
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
        stalls.Setup(s => s.GetContractAttentionAsOfAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContractAttentionDto>
            {
                new(Guid.NewGuid(), FacilityCode.ICE, "02", "Luz Mendoza", new DateOnly(2022, 11, 30), new DateOnly(2025, 11, 30), IsExpired: true),
            });

        stalls.Setup(s => s.GetClosedStallAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EEMOCantilanSDS.Application.Dtos.Stalls.ClosedStallAccountDto(
                    Guid.NewGuid(), InactiveAccountState.Expired, FacilityCode.ICE, "Iceplant", "02",
                    "Luz Mendoza", "Luz Mendoza", new DateOnly(2022, 11, 30), 3, 1_200m, null,
                    new DateOnly(2025, 11, 30), 0m, 5_000m, null)
            });

        var online = new Mock<IOnlinePaymentRepository>();
        online.Setup(o => o.GetAwaitingOrByPeriodAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OnlinePaymentAwaitingOrDto>
            {
                new(Guid.NewGuid(), "REF-1", FacilityCode.NCC, "07", "Ana Lim", "2025-12", 3_240m, "GCash", DateTime.UtcNow),
            });

        var payments = new Mock<IPaymentRepository>();
        payments.Setup(p => p.GetUnreceiptedCashPaymentsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UnreceiptedPaymentDto>());
        payments.Setup(p => p.GetUnreceiptedCashPaymentsForYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UnreceiptedPaymentDto>());

        var slaughter = new Mock<ISlaughterRepository>();
        slaughter.Setup(s => s.GetTransactionsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SlaughterTransactionDto>());
        slaughter.Setup(s => s.GetTransactionsByYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SlaughterTransactionDto>());

        var trm = new Mock<ITrmRepository>();
        trm.Setup(t => t.GetTripsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TrmTripDto>());
        trm.Setup(t => t.GetTripsByYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TrmTripDto>());

        var tpm = new Mock<ITpmRepository>();
        tpm.Setup(t => t.GetMonthAttendanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TpmVendorAttendanceDto>());
        tpm.Setup(t => t.GetYearAttendanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TpmVendorAttendanceDto>());

        var utilities = new Mock<IUtilityBillRepository>();
        utilities.Setup(u => u.GetForMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UtilityBill>());

        var handler = new GetFollowUpHistoryQueryHandler(
            reports.Object,
            stalls.Object,
            online.Object,
            payments.Object,
            slaughter.Object,
            trm.Object,
            tpm.Object,
            utilities.Object,
            CacheTestDoubles.PassthroughCache,
            CacheTestDoubles.Tenant,
            new EemoCacheOptions());

        return (handler, stalls, online, payments, slaughter);
    }

    [Fact]
    public async Task Composes_PastPeriodSnapshot_UsingPeriodScopedSources()
    {
        var (handler, stalls, online, _, _) = Build();

        var result = await handler.Handle(new GetFollowUpHistoryQuery(2025, 12), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;

        // Snapshot date is stamped as the LAST day of the requested period.
        Assert.Equal(new DateOnly(2025, 12, 31), dto.AsOf);
        Assert.Equal("December 2025", dto.PeriodLabel);

        var items = dto.Items;

        // Same composition rules as the live queue.
        var delinquent = Assert.Single(items, i => i.ReasonKind == "delinquent");
        Assert.Equal(1, delinquent.Section);
        var arrears = Assert.Single(items, i => i.ReasonKind == "arrears");
        Assert.Equal(2, arrears.Section);
        var current = Assert.Single(items, i => i.ReasonKind == "current");
        Assert.Contains("12", current.Identifier);          // stall 09 deduped (already arrears)
        var excused = Assert.Single(items, i => i.ReasonKind == "excused");
        Assert.Equal(3, excused.Section);
        Assert.True(excused.Excused);
        var contract = Assert.Single(items, i => i.ReasonKind == "contract");
        Assert.Equal("/profile/ice/02", contract.Link);
        // Expired row surfaces its full outstanding balance (from the Closed Accounts register).
        Assert.Equal(5_000m, contract.Amount);
        var missingOr = Assert.Single(items, i => i.ReasonKind == "missingor");
        Assert.Equal("Encode OR", missingOr.Action);
        Assert.Equal(3_240m, missingOr.Amount);

        // Period-scoped sources were used with the requested period — NOT the live "as of today" ones.
        stalls.Verify(s => s.GetContractAttentionAsOfAsync(2025, 12, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        stalls.Verify(s => s.GetContractAttentionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        online.Verify(o => o.GetAwaitingOrByPeriodAsync(2025, 12, It.IsAny<CancellationToken>()), Times.Once);
        online.Verify(o => o.GetAwaitingOrAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WholeYear_AggregatesMissingOr_AcrossMonths_UsingYearWideSource()
    {
        var (handler, _, _, payments, _) = Build();

        // Two blank-OR NPM whole-month settlements in DIFFERENT months (Mar + May) — the case the
        // single-month "as of June" view could never show.
        payments.Setup(p => p.GetUnreceiptedCashPaymentsForYearAsync(2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new UnreceiptedPaymentDto(FacilityCode.NPM, "3", "Ramil C. Orjeles", 930m, 31, IsDaily: true, StallId: Guid.NewGuid(), Year: 2026, Month: 3),
                new UnreceiptedPaymentDto(FacilityCode.NPM, "30", "Joel I. Maligsa", 900m, 30, IsDaily: true, StallId: Guid.NewGuid(), Year: 2026, Month: 5),
            });

        var result = await handler.Handle(new GetFollowUpHistoryQuery(2026, 6, WholeYear: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var daily = result.Value!.Items
            .Where(i => i.ReasonKind == "missingor" && i.Reason == "Daily receipt · OR")
            .ToList();

        Assert.Equal(2, daily.Count);
        // Each row is labelled with its OWN month (not the snapshot's June), so the Add-OR modal opens right.
        Assert.Contains(daily, i => i.Person == "Ramil C. Orjeles" && i.Period == "March 2026" && i.Amount == 930m);
        Assert.Contains(daily, i => i.Person == "Joel I. Maligsa" && i.Period == "May 2026" && i.Amount == 900m);

        // The year-wide source was used — the single-month one must NOT be called for a whole-year request.
        payments.Verify(p => p.GetUnreceiptedCashPaymentsForYearAsync(2026, It.IsAny<CancellationToken>()), Times.Once);
        payments.Verify(p => p.GetUnreceiptedCashPaymentsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WholeYear_AggregatesServiceFacilityMissingOr_PerRecordMonth()
    {
        var (handler, _, _, _, slaughter) = Build();

        // Blank-OR slaughter receipts for the SAME owner in TWO different months (Feb + Aug).
        slaughter.Setup(s => s.GetTransactionsByYearAsync(2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new SlaughterTransactionDto(Guid.NewGuid(), "Ana Reyes", AnimalType.Hog, null, 1, 250m, 250m, null, new DateOnly(2026, 2, 3)),
                new SlaughterTransactionDto(Guid.NewGuid(), "Ana Reyes", AnimalType.Cow, null, 1, 365m, 365m, null, new DateOnly(2026, 8, 9)),
            });

        var result = await handler.Handle(new GetFollowUpHistoryQuery(2026, 6, WholeYear: true), CancellationToken.None);
        Assert.True(result.IsSuccess);

        var slh = result.Value!.Items
            .Where(i => i.ReasonKind == "missingor" && i.Facility == FacilityCode.SLH)
            .ToList();

        // One row PER record month (each opens its own month in Add-OR), not one merged row.
        Assert.Equal(2, slh.Count);
        Assert.Contains(slh, i => i.Period == "February 2026" && i.Amount == 250m);
        Assert.Contains(slh, i => i.Period == "August 2026" && i.Amount == 365m);

        slaughter.Verify(s => s.GetTransactionsByYearAsync(2026, It.IsAny<CancellationToken>()), Times.Once);
        slaughter.Verify(s => s.GetTransactionsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
