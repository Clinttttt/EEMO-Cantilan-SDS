using EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// SettleNpmDays records specific NPM days of one stall as paid in one transaction and stamps ONE
/// Official Receipt across every settled day (a single physical receipt covering the selected days).
/// </summary>
public class SettleNpmDaysCommandHandlerTests
{
    private static Stall NpmStallWithContract(DateOnly effectivity, int years)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        stall.Contracts.Add(Contract.Create(stall.Id, "Vendor", "Vendor", effectivity, years, 900m));
        return stall;
    }

    /// <summary>
    /// Settling days never charges more than the month itself owes.
    /// </summary>
    /// <remarks>
    /// Found by audit 2026-09-08. Where the office lets a stall for a monthly rent, the month owes that rent whatever its calendar
    /// gave it: a 31-day month at ₱30 owes ₱900, not ₱930. This handler charged every selected day its own fee with no ceiling, so
    /// settling a whole 31-day month took ₱930 — thirty pesos MORE THAN THE PAYOR OWED — while the arrears screen that offered those
    /// days quoted ₱900, because it prices them as Math.Min(listed, month payable).
    ///
    /// <para>The office over-collected and its own screen disagreed with its ledger. The ceiling now comes from the SAME settlement
    /// the quote uses, so the two are one number by construction rather than by two calculations happening to agree.</para>
    ///
    /// <para>A day beyond the ceiling is left alone rather than settled at a reduced fee: the month's rent is met, so nothing is owed
    /// for that day, and recording a part-fee against it would misstate what the office received on the day.</para>
    /// </remarks>
    [Fact]
    public async Task Settle_NeverChargesMoreThanTheMonthOwes()
    {
        var today = PhilippineTime.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var stall = NpmStallWithContract(monthStart.AddMonths(-3), 3);

        // Every day of the month offered at once — the case a collector clearing a whole month's arrears produces.
        var daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
        var everyDay = Enumerable.Range(0, daysInMonth).Select(monthStart.AddDays).ToArray();

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var closureRepo = new Mock<INpmMarketClosureRepository>();
        var uow = new Mock<IUnitOfWork>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        closureRepo.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());
        paymentRepo.Setup(r => r.IsDailyCollectionOrAvailableForStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        currentUser.SetupGet(c => c.Username).Returns("admin");

        var captured = new List<DailyCollection>();
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured.Add(dc))
            .Returns(Task.CompletedTask);

        // The month owes ₱900, which is what the arrears screen quotes for it.
        var handler = new SettleNpmDaysCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object,
            closureRepo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver,
            CacheTestDoubles.MonthSettlementCappedAt(900m), CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

        var result = await handler.Handle(
            new SettleNpmDaysCommand(stall.Id, everyDay, "OR-CAP"), CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Never past the month's own figure. Uncapped this took 31 × ₱30 = ₱930 on a 31-day month.
        var charged = captured.Sum(dc => dc.DailyFee);
        Assert.True(charged <= 900m, $"charged {charged} against a month that owes 900");
        Assert.Equal(900m, charged);
    }

    [Fact]
    public async Task Settle_MarksSelectedDaysPaid_AndStampsOneOrOnAll()
    {
        var today = PhilippineTime.Today;
        var d1 = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);   // a fully-past month, day 1
        var d2 = d1.AddDays(1);
        var stall = NpmStallWithContract(d1.AddMonths(-3), 3);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var closureRepo = new Mock<INpmMarketClosureRepository>();
        var uow = new Mock<IUnitOfWork>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        closureRepo.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());
        paymentRepo.Setup(r => r.IsDailyCollectionOrAvailableForStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        currentUser.SetupGet(c => c.Username).Returns("admin");

        var captured = new List<DailyCollection>();
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured.Add(dc))
            .Returns(Task.CompletedTask);

        var handler = new SettleNpmDaysCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object,
            closureRepo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.MonthSettlement, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

        var result = await handler.Handle(
            new SettleNpmDaysCommand(stall.Id, new[] { d1, d2 }, "OR-DAYS"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, captured.Count);
        Assert.All(captured, dc => Assert.True(dc.IsPaid));
        Assert.All(captured, dc => Assert.Equal("OR-DAYS", dc.ORNumber));   // one receipt covers both days
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Settle_CollectsADayLeftOwingByALesseeWhoHasSinceBeenReplaced()
    {
        // The office is collecting arrears from a former lessee of a stall somebody else now holds. The day belongs
        // to that former lessee's term, so it must settle — while the check remains a real one: a day nobody's term
        // answers for is still refused (see the next test).
        var today = PhilippineTime.Today;
        var handover = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var theirDay = handover.AddDays(-10);                              // inside the ended term

        var stall = NpmStallWithContract(handover.AddYears(-2), 3);        // the outgoing lessee's term
        var outgoing = stall.Contracts.Single();
        outgoing.Terminate("Head", handover.AddDays(-1));
        stall.Contracts.Add(Contract.Create(stall.Id, "New Vendor", "New Vendor", handover, 3, 900m));

        var (handler, captured, _) = BuildSettleHandler(stall);

        var result = await handler.Handle(
            new SettleNpmDaysCommand(stall.Id, new[] { theirDay }, "OR-PAST"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var settled = Assert.Single(captured);
        Assert.Equal(theirDay, settled.CollectionDate);
        Assert.True(settled.IsPaid);
    }

    [Fact]
    public async Task Settle_SkipsADayNobodysTermAnswersFor()
    {
        // Between two lessees, or after a term lapsed, nothing is owed — such a day must not become a collection.
        var today = PhilippineTime.Today;
        var lapsedStart = new DateOnly(today.Year, today.Month, 1).AddYears(-3);

        var stall = NpmStallWithContract(lapsedStart, 1);                  // term ran out two years ago
        var afterTheTerm = stall.Contracts.Single().ExpiryDate.AddDays(30);

        var (handler, captured, uow) = BuildSettleHandler(stall);

        var result = await handler.Handle(
            new SettleNpmDaysCommand(stall.Id, new[] { afterTheTerm }, "OR-NONE"), CancellationToken.None);

        // The clerk is told, rather than the day quietly becoming a collection nobody owed.
        Assert.False(result.IsSuccess);
        Assert.Empty(captured);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The handler with every collaborator stubbed, and the collections it creates captured.</summary>
    private static (SettleNpmDaysCommandHandler handler, List<DailyCollection> captured, Mock<IUnitOfWork> uow)
        BuildSettleHandler(Stall stall)
    {
        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var closureRepo = new Mock<INpmMarketClosureRepository>();
        var uow = new Mock<IUnitOfWork>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DailyCollection>());
        closureRepo.Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());
        paymentRepo.Setup(r => r.IsDailyCollectionOrAvailableForStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        currentUser.SetupGet(c => c.Username).Returns("admin");

        var captured = new List<DailyCollection>();
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured.Add(dc))
            .Returns(Task.CompletedTask);

        var handler = new SettleNpmDaysCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object,
            closureRepo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.MonthSettlement, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

        return (handler, captured, uow);
    }

    [Fact]
    public async Task Settle_RejectsNonNpmStall()
    {
        var stall = Stall.Create(Guid.NewGuid(), "101", 2400m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC"));

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("admin");

        var handler = new SettleNpmDaysCommandHandler(
            new Mock<IDailyCollectionRepository>().Object, new Mock<IPaymentRepository>().Object, stallRepo.Object,
            new Mock<ICollectorRepository>().Object, currentUser.Object, new Mock<INpmMarketClosureRepository>().Object,
            new Mock<IUnitOfWork>().Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.MonthSettlement, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

        var result = await handler.Handle(
            new SettleNpmDaysCommand(stall.Id, new[] { new DateOnly(2026, 6, 1) }, null), CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }
}
