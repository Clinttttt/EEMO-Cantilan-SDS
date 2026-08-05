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
        // Iceplant stall 02 is ONE stall, so every source names it by the same identity — which is what lets the
        // composer tell it apart from a different stall that happens to share a number. The market numbers spaces
        // per section, so NPM really does have three stalls called "1".
        var iceStall02 = Guid.NewGuid();
        stalls.Setup(s => s.GetContractAttentionAsOfAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContractAttentionDto>
            {
                new(iceStall02, FacilityCode.ICE, "02", "Luz Mendoza", new DateOnly(2022, 11, 30), new DateOnly(2025, 11, 30), IsExpired: true),
            });

        // The cumulative "Whole time" view reads contract attention as of today rather than as of a period.
        stalls.Setup(s => s.GetContractAttentionAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ContractAttentionDto>
            {
                new(iceStall02, FacilityCode.ICE, "02", "Luz Mendoza", new DateOnly(2022, 11, 30), new DateOnly(2025, 11, 30), IsExpired: true),
            });

        stalls.Setup(s => s.GetClosedStallAccountsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EEMOCantilanSDS.Application.Dtos.Stalls.ClosedStallAccountDto(
                    iceStall02, InactiveAccountState.Expired, FacilityCode.ICE, "Iceplant", "02",
                    "Luz Mendoza", "Luz Mendoza", new DateOnly(2022, 11, 30), 3, 1_200m, null,
                    new DateOnly(2025, 11, 30), 0m, 5_000m, null)
            });

        // A period-scoped snapshot reads the register bounded to that period: the same accounts, with the figures
        // that period assessed. Here it returns the same row so the composition rules can be compared directly.
        stalls.Setup(s => s.GetClosedStallAccountsForPeriodAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EEMOCantilanSDS.Application.Dtos.Stalls.ClosedStallAccountDto(
                    iceStall02, InactiveAccountState.Expired, FacilityCode.ICE, "Iceplant", "02",
                    "Luz Mendoza", "Luz Mendoza", new DateOnly(2022, 11, 30), 3, 1_200m, null,
                    new DateOnly(2025, 11, 30), 0m, 1_200m, null)
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
    public async Task TheWholeTimeView_StatesTheLifetimeBalance_WhereAPeriodViewStatesThePeriods()
    {
        // The two views answer different questions and must not borrow each other's figures: "what is owed in total"
        // is the whole-time reading, and a year or month states only its own.
        var (handler, _, _, _, _) = Build();

        var allTime = await handler.Handle(new GetFollowUpHistoryQuery(2025, 12, AllTime: true), CancellationToken.None);

        Assert.True(allTime.IsSuccess);
        Assert.Equal("Whole time", allTime.Value!.PeriodLabel);

        // The inactive-account balance is stated in full here — one row for the account, its own lifetime figure.
        var row = Assert.Single(allTime.Value.Items, i => i.Amount == 5_000m);
        Assert.Equal("Stall 02", row.Identifier);
        // …and the term's WHOLE span is the period beside that lifetime figure. This is the one view where it is
        // the right reading; a year or a month states its own period instead.
        Assert.Equal("Nov 2022 → Nov 30, 2025", row.Period);
    }

    [Fact]
    public async Task APeriodSnapshot_ReadsTheRegisterBoundedToThatPeriod_NotTheLifetimeOne()
    {
        var (handler, stalls, _, _, _) = Build();

        var result = await handler.Handle(new GetFollowUpHistoryQuery(2026, 7, WholeYear: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // A whole-year window runs from January to the snapshot date — never into the future — and the figures come
        // from the register bounded to it. Reading the lifetime register here is what put a lifetime balance under a
        // single year's heading.
        stalls.Verify(s => s.GetClosedStallAccountsForPeriodAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 31), It.IsAny<CancellationToken>()), Times.Once);
        stalls.Verify(s => s.GetClosedStallAccountsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AMonthSnapshot_BoundsTheRegisterToThatMonth()
    {
        var (handler, stalls, _, _, _) = Build();

        await handler.Handle(new GetFollowUpHistoryQuery(2025, 12), CancellationToken.None);

        stalls.Verify(s => s.GetClosedStallAccountsForPeriodAsync(
            new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TheWholeTimeView_ReadsTheLifetimeRegister()
    {
        var (handler, stalls, _, _, _) = Build();

        await handler.Handle(new GetFollowUpHistoryQuery(2025, 12, AllTime: true), CancellationToken.None);

        stalls.Verify(s => s.GetClosedStallAccountsAsync(It.IsAny<CancellationToken>()), Times.Once);
        stalls.Verify(s => s.GetClosedStallAccountsForPeriodAsync(
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
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
        // Both stalls state their period balance: the arrears figure covers elapsed months and excludes the month
        // in progress, so a stall behind on past months can also owe the current one.
        var current = items.Where(i => i.ReasonKind == "current").ToList();
        Assert.Equal(2, current.Count);
        Assert.Contains(current, i => i.Identifier.Contains("12"));
        Assert.Contains(current, i => i.Identifier.Contains("09"));
        var excused = Assert.Single(items, i => i.ReasonKind == "excused");
        Assert.Equal(3, excused.Section);
        Assert.True(excused.Excused);
        var contract = Assert.Single(items, i => i.ReasonKind == "contract");
        Assert.Equal("/profile/ice/02", contract.Link);
        // A period-scoped snapshot states its own period, with the lapse date in the status line. This term ran out
        // in November, before the December window, so its span is stated as the term it was — clipping a term that
        // lies wholly outside the period would leave one meaningless day.
        Assert.Equal("Nov 2022 → Nov 30, 2025", contract.Period);
        Assert.Equal("Expired Nov 30, 2025 · active occupant", contract.Status);
        // A view scoped to a period states that period's figure. This stall has no assessed balance for December,
        // so the row carries none — the register's lifetime ₱5,000 belongs to "Whole time", the view that exists to
        // answer what is owed in total, and showing it here read as a lifetime total under a single month's heading.
        Assert.Null(contract.Amount);
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
