using EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;
using EEMOCantilanSDS.Application.Command.Payments.RestoreNpmOperatingDay;
using EEMOCantilanSDS.Application.Command.Payments.SetMarketClosure;
using EEMOCantilanSDS.Application.Command.Payments.SetNpmOperatingSchedule;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Market-closure set is an upsert (no duplicate per date); clear is idempotent. With a weekly schedule in force, clearing a
/// scheduled-closed day opens that one day, closing a scheduled day records nothing, and a past day is the Head's with a reason.
/// </summary>
public class NpmMarketClosureCommandHandlerTests
{
    // "Today" is a Monday before the dates used below, so the ordinary tests act on future days.
    private static readonly DateOnly Today = new(2026, 6, 1);
    private static readonly DateOnly Saturday = new(2026, 6, 13);

    private sealed class Fixture
    {
        public Mock<INpmMarketClosureRepository> Closures { get; } = new();
        public Mock<INpmOperatingScheduleRepository> Schedules { get; } = new();
        public Mock<ICurrentUserService> User { get; } = new();
        public Mock<IClock> Clock { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();

        public Fixture(string role = "Admin")
        {
            User.SetupGet(c => c.IsAuthenticated).Returns(true);
            User.SetupGet(c => c.Role).Returns(role);
            User.SetupGet(c => c.Username).Returns("tester");
            Clock.SetupGet(c => c.PhilippineToday).Returns(Today);
            Schedules.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<NpmOperatingSchedule>());
            Closures.Setup(r => r.GetAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((NpmMarketClosure?)null);
        }

        public Fixture WithWeekendSchedule()
        {
            Schedules.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                [NpmOperatingSchedule.Create(Today, [DayOfWeek.Saturday, DayOfWeek.Sunday], "Head memorandum", "head")]);
            return this;
        }

        public Fixture WithExisting(NpmMarketClosure row)
        {
            Closures.Setup(r => r.GetAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(row);
            return this;
        }

        public SetNpmMarketClosureCommandHandler Set() =>
            new(Closures.Object, Schedules.Object, User.Object, Clock.Object, Uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        public ClearNpmMarketClosureCommandHandler Clear() =>
            new(Closures.Object, Schedules.Object, User.Object, Clock.Object, Uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        public RestoreNpmOperatingDayCommandHandler Restore() =>
            new(Closures.Object, User.Object, Clock.Object, Uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        public SetNpmOperatingScheduleCommandHandler SetSchedule() =>
            new(Schedules.Object, User.Object, Clock.Object, Uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);
    }

    [Fact]
    public async Task Set_WhenNoneExists_CreatesClosure()
    {
        var f = new Fixture();

        var result = await f.Set().Handle(new SetNpmMarketClosureCommand(new DateOnly(2026, 6, 15), MarketClosureReason.Holiday), CancellationToken.None);

        Assert.True(result.IsSuccess);
        f.Closures.Verify(r => r.AddAsync(It.IsAny<NpmMarketClosure>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_WhenExists_UpdatesInPlace_NoDuplicate()
    {
        var existing = NpmMarketClosure.Create(new DateOnly(2026, 6, 15));
        var f = new Fixture().WithExisting(existing);

        var result = await f.Set().Handle(new SetNpmMarketClosureCommand(new DateOnly(2026, 6, 15), MarketClosureReason.Weather, "Typhoon"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        f.Closures.Verify(r => r.AddAsync(It.IsAny<NpmMarketClosure>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(MarketClosureReason.Weather, existing.Reason);
        Assert.Equal("Typhoon", existing.Remarks);
    }

    [Fact]
    public async Task Clear_WhenExists_RemovesClosure()
    {
        var existing = NpmMarketClosure.Create(new DateOnly(2026, 6, 15));
        var f = new Fixture().WithExisting(existing);

        var result = await f.Clear().Handle(new ClearNpmMarketClosureCommand(new DateOnly(2026, 6, 15)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        f.Closures.Verify(r => r.Remove(existing), Times.Once);
    }

    [Fact]
    public async Task Clear_WhenNone_IsIdempotent()
    {
        var f = new Fixture();

        var result = await f.Clear().Handle(new ClearNpmMarketClosureCommand(new DateOnly(2026, 6, 15)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        f.Closures.Verify(r => r.Remove(It.IsAny<NpmMarketClosure>()), Times.Never);
        f.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpeningAScheduledWeekend_RecordsAnExplicitOpening_AndNeverEditsTheSchedule()
    {
        var f = new Fixture().WithWeekendSchedule();
        NpmMarketClosure? added = null;
        f.Closures.Setup(r => r.AddAsync(It.IsAny<NpmMarketClosure>(), It.IsAny<CancellationToken>())).Callback<NpmMarketClosure, CancellationToken>((c, _) => added = c);

        var result = await f.Clear().Handle(new ClearNpmMarketClosureCommand(Saturday, "Working Saturday"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.True(added!.IsOpenOverride);
        Assert.Equal(Saturday, added.ClosureDate);
        f.Schedules.Verify(s => s.AddAsync(It.IsAny<NpmOperatingSchedule>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpeningAScheduledDayThatHasAnExplicitClosure_TurnsThatRowIntoAnOpening()
    {
        var closure = NpmMarketClosure.Create(Saturday, MarketClosureReason.Holiday, "Holiday", "head");
        var f = new Fixture().WithWeekendSchedule().WithExisting(closure);

        await f.Clear().Handle(new ClearNpmMarketClosureCommand(Saturday), CancellationToken.None);

        Assert.True(closure.IsOpenOverride);
        f.Closures.Verify(r => r.Remove(It.IsAny<NpmMarketClosure>()), Times.Never);
        f.Closures.Verify(r => r.AddAsync(It.IsAny<NpmMarketClosure>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClosingADayTheScheduleAlreadyCloses_RecordsNothing()
    {
        var f = new Fixture().WithWeekendSchedule();

        var result = await f.Set().Handle(new SetNpmMarketClosureCommand(Saturday, MarketClosureReason.Holiday), CancellationToken.None);

        Assert.True(result.IsSuccess);
        f.Closures.Verify(r => r.AddAsync(It.IsAny<NpmMarketClosure>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClosingADayTheHeadHadOpened_ReturnsItToTheSchedule()
    {
        var opening = NpmMarketClosure.CreateOpenOverride(Saturday, "Working Saturday", "head");
        var f = new Fixture().WithWeekendSchedule().WithExisting(opening);

        await f.Set().Handle(new SetNpmMarketClosureCommand(Saturday, MarketClosureReason.Holiday), CancellationToken.None);

        f.Closures.Verify(r => r.Remove(opening), Times.Once);
    }

    [Fact]
    public async Task ClosingAWeekdayOverridesAnOpenSchedule_AndANormalWeekdayOpeningRemovesTheClosure()
    {
        var weekday = new DateOnly(2026, 6, 17);
        var f = new Fixture().WithWeekendSchedule();
        await f.Set().Handle(new SetNpmMarketClosureCommand(weekday, MarketClosureReason.Holiday), CancellationToken.None);
        f.Closures.Verify(r => r.AddAsync(It.Is<NpmMarketClosure>(c => c.ClosureDate == weekday && !c.IsOpenOverride), It.IsAny<CancellationToken>()), Times.Once);

        var closure = NpmMarketClosure.Create(weekday);
        var g = new Fixture().WithWeekendSchedule().WithExisting(closure);
        await g.Clear().Handle(new ClearNpmMarketClosureCommand(weekday), CancellationToken.None);
        g.Closures.Verify(r => r.Remove(closure), Times.Once);
    }

    [Fact]
    public async Task APastDay_NeedsTheHead_AndAReason()
    {
        var past = new DateOnly(2026, 5, 20);

        var admin = new Fixture("Admin");
        var refusedAdmin = await admin.Clear().Handle(new ClearNpmMarketClosureCommand(past, "Rain closed the market"), CancellationToken.None);
        Assert.False(refusedAdmin.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, refusedAdmin.Status);

        var head = new Fixture("SuperAdmin");
        var noReason = await head.Set().Handle(new SetNpmMarketClosureCommand(past, MarketClosureReason.Weather), CancellationToken.None);
        Assert.False(noReason.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, noReason.Status);
        head.Closures.Verify(r => r.AddAsync(It.IsAny<NpmMarketClosure>(), It.IsAny<CancellationToken>()), Times.Never);

        var accepted = await head.Set().Handle(new SetNpmMarketClosureCommand(past, MarketClosureReason.Weather, "Typhoon closed the market"), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
    }

    [Fact]
    public async Task Restore_RemovesTheExplicitRow_SoTheScheduleDecidesAgain()
    {
        var opening = NpmMarketClosure.CreateOpenOverride(Saturday, "Working Saturday", "head");
        var f = new Fixture().WithWeekendSchedule().WithExisting(opening);

        var result = await f.Restore().Handle(new RestoreNpmOperatingDayCommand(Saturday), CancellationToken.None);

        Assert.True(result.IsSuccess);
        f.Closures.Verify(r => r.Remove(opening), Times.Once);
    }

    [Fact]
    public async Task TheWeeklySchedule_IsTheHeadsAlone_AndProspective()
    {
        var weekend = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday };

        var admin = new Fixture("Admin");
        Assert.Equal(ResultStatus.Forbidden, (await admin.SetSchedule().Handle(new SetNpmOperatingScheduleCommand(Today, weekend, "Memo"), CancellationToken.None)).Status);

        var head = new Fixture("SuperAdmin");
        var past = await head.SetSchedule().Handle(new SetNpmOperatingScheduleCommand(Today.AddDays(-1), weekend, "Memo"), CancellationToken.None);
        Assert.Equal(ResultStatus.Invalid, past.Status);
        var noAuthority = await head.SetSchedule().Handle(new SetNpmOperatingScheduleCommand(Today, weekend, null), CancellationToken.None);
        Assert.Equal(ResultStatus.Invalid, noAuthority.Status);
        var everyDay = await head.SetSchedule().Handle(new SetNpmOperatingScheduleCommand(Today, Enum.GetValues<DayOfWeek>(), "Memo"), CancellationToken.None);
        Assert.Equal(ResultStatus.Invalid, everyDay.Status);
        head.Schedules.Verify(s => s.AddAsync(It.IsAny<NpmOperatingSchedule>(), It.IsAny<CancellationToken>()), Times.Never);

        var ok = await head.SetSchedule().Handle(new SetNpmOperatingScheduleCommand(Today.AddDays(7), weekend, "Head memorandum 2026-11"), CancellationToken.None);
        Assert.True(ok.IsSuccess);
        head.Schedules.Verify(s => s.AddAsync(It.Is<NpmOperatingSchedule>(x => x.EffectiveFrom == Today.AddDays(7) && x.IsClosedOn(DayOfWeek.Saturday)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AScheduleAlreadyInForceIsHistory_AnUpcomingOneMayBeRestated_AndAnEmptyOneNeedsNoAuthority()
    {
        var inForce = NpmOperatingSchedule.Create(Today, [DayOfWeek.Sunday], "Memo", "head");
        var head = new Fixture("SuperAdmin");
        head.Schedules.Setup(s => s.GetByEffectiveDateAsync(Today, It.IsAny<CancellationToken>())).ReturnsAsync(inForce);
        var conflict = await head.SetSchedule().Handle(new SetNpmOperatingScheduleCommand(Today, [DayOfWeek.Saturday], "Memo"), CancellationToken.None);
        Assert.Equal(ResultStatus.Conflict, conflict.Status);
        Assert.True(inForce.IsClosedOn(DayOfWeek.Sunday) && !inForce.IsClosedOn(DayOfWeek.Saturday));

        var upcoming = NpmOperatingSchedule.Create(Today.AddDays(7), [DayOfWeek.Sunday], "Memo", "head");
        head.Schedules.Setup(s => s.GetByEffectiveDateAsync(Today.AddDays(7), It.IsAny<CancellationToken>())).ReturnsAsync(upcoming);
        Assert.True((await head.SetSchedule().Handle(new SetNpmOperatingScheduleCommand(Today.AddDays(7), [DayOfWeek.Saturday, DayOfWeek.Sunday], "Memo v2"), CancellationToken.None)).IsSuccess);
        Assert.True(upcoming.IsClosedOn(DayOfWeek.Saturday));

        var restore = await head.SetSchedule().Handle(new SetNpmOperatingScheduleCommand(Today.AddDays(30), [], null), CancellationToken.None);
        Assert.True(restore.IsSuccess);
    }
}
