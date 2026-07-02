using EEMOCantilanSDS.Application.Command.Stalls.ToggleStallStatus;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
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

        return (new ToggleStallStatusCommandHandler(stallRepo.Object, monthly.Object, daily.Object, payments.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant),
                monthly, daily, payments);
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
