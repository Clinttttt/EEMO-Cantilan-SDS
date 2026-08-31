using EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Closing a stall freezes it (records the close date, no obligation while closed); reopening resumes
/// and persists the frozen span as EXCUSED so it is never back-billed — monthly facilities get an
/// excused billing month per closed month, NPM gets an absent (₱0) day per closed day.
/// </summary>
public class ToggleStallStatusCommandHandlerTests
{
    private static Stall StallInFacility(FacilityCode code, decimal rate = 2400m)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", rate, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(code, code.ToString(), code.ToString()));
        stall.Contracts.Add(Contract.Create(stall.Id, "Occupant", "Occupant", new DateOnly(2024, 1, 1), 5, rate));
        return stall;
    }

    private static (ToggleStallStatusCommandHandler handler,
                    Mock<IStallMonthlyExceptionRepository> monthly,
                    Mock<IDailyCollectionRepository> daily,
                    Mock<IPaymentRepository> payments) Build(Stall stall)
    {
        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);

        var monthly = new Mock<IStallMonthlyExceptionRepository>();
        monthly.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StallMonthlyException?)null);

        var daily = new Mock<IDailyCollectionRepository>();
        daily.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);

        // No prior payment by default — every closed month is therefore excused.
        var payments = new Mock<IPaymentRepository>();
        payments.Setup(r => r.GetPaymentRecordAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentRecordDto?)null);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("tester");
        var uow = new Mock<IUnitOfWork>();

        // The handler reads one thing from the context: whether this stall's own market section has been closed. An empty
        // in-memory context therefore means "no section is closed", which is what every test here but one assumes.
        var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        return (new ToggleStallStatusCommandHandler(stallRepo.Object, monthly.Object, daily.Object, payments.Object, context, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow)),
                monthly, daily, payments);
    }

    [Fact]
    public async Task AStallInAClosedSectionRefusesToResumeOnItsOwn()
    {
        // The office closed the section, which closed this stall with it. Resuming the stall alone would start billing a
        // space the market page does not show and no form offers, so the money would accrue where nobody can see it. The
        // office reopens the SECTION, which returns every stall that closure closed.
        //
        // Refused on the server and not only greyed out on the screen: a disabled button is not a guard.
        var stall = Stall.Create(
            Guid.NewGuid(), "12", 900m, ApplicableFees.DailyRental,
            dailyRate: 27m, customSectionName: "Sari-sari Area");
        stall.Close(new DateOnly(2026, 8, 30));

        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        await using (var seed = new AppDbContext(options))
        {
            seed.FacilitySectionClosures.Add(FacilitySectionClosure.Create(
                FacilityCode.NPM, "Sari-sari Area", new DateOnly(2026, 8, 30), new[] { stall.Id }, Guid.NewGuid()));
            await seed.SaveChangesAsync();
        }

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("tester");

        await using var context = new AppDbContext(options);
        var handler = new ToggleStallStatusCommandHandler(
            stallRepo.Object,
            Mock.Of<IStallMonthlyExceptionRepository>(),
            Mock.Of<IDailyCollectionRepository>(),
            Mock.Of<IPaymentRepository>(),
            context,
            currentUser.Object,
            Mock.Of<IUnitOfWork>(),
            CacheTestDoubles.Invalidator,
            CacheTestDoubles.Tenant,
            new FixedClock(new DateTime(2026, 9, 5)));

        var result = await handler.Handle(new ToggleStallStatusCommand(stall.Id, Close: false), default);

        Assert.False(result.IsSuccess);
        Assert.Contains("Sari-sari Area is closed", result.Error);
        Assert.Equal(StallStatus.Closed, stall.Status);              // left exactly as it was
        Assert.Equal(new DateOnly(2026, 8, 30), stall.ClosedAt);     // and its closing day is not rewritten
    }

    [Fact]
    public async Task AStallInAnOPENSectionResumesNormally()
    {
        // The same stall, with no closure on its section: the guard is about closed sections only, and must not become a
        // reason a custom-section stall can never be resumed.
        var stall = Stall.Create(
            Guid.NewGuid(), "12", 900m, ApplicableFees.DailyRental,
            dailyRate: 27m, customSectionName: "Sari-sari Area");
        stall.Close(new DateOnly(2026, 8, 30));

        var (handler, _, _, _) = Build(stall);

        var result = await handler.Handle(new ToggleStallStatusCommand(stall.Id, Close: false), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(StallStatus.Active, stall.Status);
    }

    [Fact]
    public async Task Close_FreezesStall_AndRecordsCloseDate()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var (handler, _, _, _) = Build(stall);

        var result = await handler.Handle(new ToggleStallStatusCommand(stall.Id, Close: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StallStatus.Closed, stall.Status);
        Assert.Equal(PhilippineTime.Today, stall.ClosedAt);
    }

    [Fact]
    public async Task Reopen_Monthly_ExcusesEveryClosedMonth()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var today = PhilippineTime.Today;
        var closedOn = today.AddMonths(-2);
        stall.Close(closedOn, "tester");   // pre-closed

        var (handler, monthly, _, _) = Build(stall);
        var captured = new List<StallMonthlyException>();
        monthly.Setup(r => r.AddAsync(It.IsAny<StallMonthlyException>(), It.IsAny<CancellationToken>()))
            .Callback<StallMonthlyException, CancellationToken>((e, _) => captured.Add(e))
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(new ToggleStallStatusCommand(stall.Id, Close: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StallStatus.Active, stall.Status);
        Assert.Null(stall.ClosedAt);

        // Expected: one excused month per month the closure [closedOn, today) touched.
        var expected = new HashSet<(int, int)>();
        var c = new DateOnly(closedOn.Year, closedOn.Month, 1);
        var last = new DateOnly(today.AddDays(-1).Year, today.AddDays(-1).Month, 1);
        while (c <= last) { expected.Add((c.Year, c.Month)); c = c.AddMonths(1); }

        Assert.Equal(expected, captured.Select(e => (e.BillingYear, e.BillingMonth)).ToHashSet());
        Assert.All(captured, e => Assert.Equal(MonthlyExceptionReason.TemporaryClosure, e.Reason));
    }

    [Fact]
    public async Task Reopen_Npm_MarksEachClosedDayAbsent()
    {
        var stall = StallInFacility(FacilityCode.NPM, rate: 900m);
        var today = PhilippineTime.Today;
        var closedOn = today.AddDays(-5);
        stall.Close(closedOn, "tester");

        var (handler, _, daily, _) = Build(stall);
        var captured = new List<DailyCollection>();
        daily.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((d, _) => captured.Add(d))
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(new ToggleStallStatusCommand(stall.Id, Close: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(StallStatus.Active, stall.Status);

        // Closed days are [closedOn, today): the 5 days before the reopen day, each marked absent.
        var expectedDays = new List<DateOnly>();
        for (var d = closedOn; d <= today.AddDays(-1); d = d.AddDays(1)) expectedDays.Add(d);

        Assert.Equal(expectedDays, captured.Select(x => x.CollectionDate).OrderBy(x => x).ToList());
        Assert.All(captured, x => Assert.True(x.IsAbsent));
    }

    [Fact]
    public async Task Reopen_Monthly_DoesNotExcuse_AMonthAlreadyPaidInFull()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var today = PhilippineTime.Today;
        var closedOn = today.AddMonths(-2);
        stall.Close(closedOn, "tester");

        var (handler, monthly, _, payments) = Build(stall);

        // The FIRST closed month was actually paid in full before the closure — it must stay "Paid", not "Excused".
        var paidMonth = new DateOnly(closedOn.Year, closedOn.Month, 1);
        payments.Setup(r => r.GetPaymentRecordAsync(stall.Id, paidMonth.Year, paidMonth.Month, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRecordDto(Guid.NewGuid(), PaymentStatus.Paid, "OR-1", 2400m, null, null, null, 2400m, 0m));

        var captured = new List<StallMonthlyException>();
        monthly.Setup(r => r.AddAsync(It.IsAny<StallMonthlyException>(), It.IsAny<CancellationToken>()))
            .Callback<StallMonthlyException, CancellationToken>((e, _) => captured.Add(e))
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(new ToggleStallStatusCommand(stall.Id, Close: false), CancellationToken.None);

        Assert.True(result.IsSuccess);

        // The already-paid month is NOT excused; the other closed month(s) still are.
        Assert.DoesNotContain((paidMonth.Year, paidMonth.Month), captured.Select(e => (e.BillingYear, e.BillingMonth)));
        Assert.NotEmpty(captured);
        Assert.All(captured, e => Assert.Equal(MonthlyExceptionReason.TemporaryClosure, e.Reason));
    }
}
