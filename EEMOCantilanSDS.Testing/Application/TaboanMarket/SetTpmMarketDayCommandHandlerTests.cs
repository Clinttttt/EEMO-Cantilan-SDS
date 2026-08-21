using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.TaboanMarket.SetMarketDay;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Services;
using EEMOCantilanSDS.Testing.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.TaboanMarket;

/// <summary>
/// Moving the weekly market to a different weekday.
///
/// <para>
/// The office asked for it plainly: the market has been held on a Friday and is moving to a Thursday. The day was
/// settable only at activation, and it is a single value, so changing it would have re-answered every date ever
/// asked about — every Friday the office had already collected would fall on a day its own system said was not a
/// market day, and it could no longer correct last week's list.
/// </para>
///
/// <para>
/// So the day is effective-dated, the way a fee rate already is here: the new day starts on a date the office
/// names, that date must be the first market day under the new arrangement, and nothing before it moves.
/// </para>
/// </summary>
public class SetTpmMarketDayCommandHandlerTests
{
    private const string TenantCode = "madrid";

    private sealed class Tenant : ITenantContext
    {
        public string? TenantCode => SetTpmMarketDayCommandHandlerTests.TenantCode;
        public void SetTenantCode(string? tenantCode) { }
    }

    private sealed class Head : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "madrid.head";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => null;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    /// <summary>An office holding its market on a Friday, as recorded at activation.</summary>
    private static async Task<Guid> SeedOfficeAsync(DbContextOptions<AppDbContext> options, DayOfWeek day = DayOfWeek.Friday)
    {
        using var seed = new AppDbContext(options);
        var municipality = Municipality.Create(
            "MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: TenantCode, tpmMarketDay: day);
        seed.Municipalities.Add(municipality);
        await seed.SaveChangesAsync();
        return municipality.Id;
    }

    private static SetTpmMarketDayCommandHandler Handler(AppDbContext ctx, DateOnly today) =>
        new(ctx,
            new TpmMarketDayProvider(ctx, new Tenant()),
            CacheTestDoubles.Invalidator,
            new Tenant(),
            new Head(),
            new FixedClock(today.ToDateTime(TimeOnly.MinValue)));

    private static TpmMarketDayProvider Provider(AppDbContext ctx) => new(ctx, new Tenant());

    // ── The office's own arrangement ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnOfficeThatHasNeverMovedItsDay_IsAnsweredFromItsRecord()
    {
        var options = Options();
        await SeedOfficeAsync(options, DayOfWeek.Wednesday);

        using var ctx = new AppDbContext(options);
        Assert.Equal(DayOfWeek.Wednesday, await Provider(ctx).GetMarketDayAsync(new DateOnly(2026, 8, 21)));
    }

    [Fact]
    public async Task AWeekAlreadyCollected_KeepsTheDayItWasHeldOn()
    {
        // The regression this design exists to prevent.
        var options = Options();
        await SeedOfficeAsync(options);
        var today = new DateOnly(2026, 8, 21);          // a Friday
        var firstThursday = new DateOnly(2026, 8, 27);

        using (var ctx = new AppDbContext(options))
        {
            var result = await Handler(ctx, today).Handle(new SetTpmMarketDayCommand(DayOfWeek.Thursday, firstThursday), default);
            Assert.True(result.IsSuccess);
        }

        using var read = new AppDbContext(options);
        var provider = Provider(read);

        // Before the change: still a Friday market.
        Assert.Equal(DayOfWeek.Friday, await provider.GetMarketDayAsync(new DateOnly(2026, 8, 14)));
        Assert.Equal(DayOfWeek.Friday, await provider.GetMarketDayAsync(new DateOnly(2026, 8, 21)));
        // From the date the office named: a Thursday market.
        Assert.Equal(DayOfWeek.Thursday, await provider.GetMarketDayAsync(firstThursday));
        Assert.Equal(DayOfWeek.Thursday, await provider.GetMarketDayAsync(new DateOnly(2026, 9, 3)));
    }

    [Fact]
    public async Task AMonthTheDayMovedIn_HasMarketDaysOnBothWeekdays()
    {
        var options = Options();
        await SeedOfficeAsync(options);

        using (var ctx = new AppDbContext(options))
        {
            await Handler(ctx, new DateOnly(2026, 8, 21))
                .Handle(new SetTpmMarketDayCommand(DayOfWeek.Thursday, new DateOnly(2026, 8, 27)), default);
        }

        using var read = new AppDbContext(options);
        var dates = await Provider(read).GetMarketDatesAsync(2026, 8);

        // Fridays up to the change, then the Thursday it starts on. August 2026: Fridays 7, 14, 21, 28.
        Assert.Contains(new DateOnly(2026, 8, 7), dates);
        Assert.Contains(new DateOnly(2026, 8, 21), dates);
        Assert.Contains(new DateOnly(2026, 8, 27), dates);
        // The Friday after the change is no longer a market day.
        Assert.DoesNotContain(new DateOnly(2026, 8, 28), dates);
    }

    // ── What the office is not allowed to do ────────────────────────────────────────────────────

    [Fact]
    public async Task ADayCannotStartInThePast()
    {
        var options = Options();
        await SeedOfficeAsync(options);

        using var ctx = new AppDbContext(options);
        var result = await Handler(ctx, new DateOnly(2026, 8, 21))
            .Handle(new SetTpmMarketDayCommand(DayOfWeek.Thursday, new DateOnly(2026, 8, 20)), default);

        Assert.False(result.IsSuccess);
        Assert.Contains("cannot start in the past", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheStartDateMustBeTheNewMarketDay()
    {
        var options = Options();
        await SeedOfficeAsync(options);

        using var ctx = new AppDbContext(options);
        // 2026-08-26 is a Wednesday, not a Thursday.
        var result = await Handler(ctx, new DateOnly(2026, 8, 21))
            .Handle(new SetTpmMarketDayCommand(DayOfWeek.Thursday, new DateOnly(2026, 8, 26)), default);

        Assert.False(result.IsSuccess);
        Assert.Contains("Thursday", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MovingToTheDayItIsAlreadyHeldOn_IsRefused()
    {
        var options = Options();
        await SeedOfficeAsync(options);

        using var ctx = new AppDbContext(options);
        var result = await Handler(ctx, new DateOnly(2026, 8, 21))
            .Handle(new SetTpmMarketDayCommand(DayOfWeek.Friday, new DateOnly(2026, 8, 28)), default);

        Assert.False(result.IsSuccess);
        Assert.Contains("already held", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ── What the rest of the portal reads ──────────────────────────────────────────────────────

    [Fact]
    public async Task TheRegistryDayMovesOnlyOnceTheNewDayHasStarted()
    {
        // The settings screen states the day the office holds the market on NOW. A change that starts next week
        // must not make this week's screen contradict this week's collections.
        var options = Options();
        await SeedOfficeAsync(options);

        using (var ctx = new AppDbContext(options))
        {
            await Handler(ctx, new DateOnly(2026, 8, 21))
                .Handle(new SetTpmMarketDayCommand(DayOfWeek.Thursday, new DateOnly(2026, 8, 27)), default);
        }

        using var read = new AppDbContext(options);
        var registered = await read.Municipalities.IgnoreQueryFilters().FirstAsync(m => m.TenantCode == TenantCode);
        Assert.Equal(DayOfWeek.Friday, registered.TpmMarketDay);
    }

    [Fact]
    public async Task AChangeStartingToday_MovesTheRegistryDayAtOnce()
    {
        var options = Options();
        await SeedOfficeAsync(options);
        var thursday = new DateOnly(2026, 8, 27);

        using (var ctx = new AppDbContext(options))
        {
            var result = await Handler(ctx, thursday).Handle(new SetTpmMarketDayCommand(DayOfWeek.Thursday, thursday), default);
            Assert.True(result.IsSuccess);
        }

        using var read = new AppDbContext(options);
        var registered = await read.Municipalities.IgnoreQueryFilters().FirstAsync(m => m.TenantCode == TenantCode);
        Assert.Equal(DayOfWeek.Thursday, registered.TpmMarketDay);
    }

    [Fact]
    public async Task TheFirstMoveRecordsTheDayTheOfficeWasAlreadyUsing()
    {
        // Without this baseline row, every date before the change would fall back to the registry record, which
        // by then holds the NEW day — and the office's collected Fridays would read as non-market days.
        var options = Options();
        await SeedOfficeAsync(options);

        using (var ctx = new AppDbContext(options))
        {
            await Handler(ctx, new DateOnly(2026, 8, 27))
                .Handle(new SetTpmMarketDayCommand(DayOfWeek.Thursday, new DateOnly(2026, 8, 27)), default);
        }

        using var read = new AppDbContext(options);
        var schedule = await read.TpmMarketDaySchedules.OrderBy(s => s.EffectiveFrom).ToListAsync();

        Assert.Equal(2, schedule.Count);
        Assert.Equal(DayOfWeek.Friday, schedule[0].Day);
        Assert.Equal(DayOfWeek.Thursday, schedule[1].Day);

        // And the office's earlier weeks still resolve to the Friday they were held on.
        Assert.Equal(DayOfWeek.Friday, await Provider(read).GetMarketDayAsync(new DateOnly(2026, 8, 14)));
    }

    [Fact]
    public async Task TheDayCanBeMovedMoreThanOnce()
    {
        var options = Options();
        await SeedOfficeAsync(options);

        using (var ctx = new AppDbContext(options))
        {
            await Handler(ctx, new DateOnly(2026, 8, 21))
                .Handle(new SetTpmMarketDayCommand(DayOfWeek.Thursday, new DateOnly(2026, 8, 27)), default);
        }
        using (var ctx = new AppDbContext(options))
        {
            var second = await Handler(ctx, new DateOnly(2026, 8, 27))
                .Handle(new SetTpmMarketDayCommand(DayOfWeek.Saturday, new DateOnly(2026, 9, 5)), default);
            Assert.True(second.IsSuccess);
        }

        using var read = new AppDbContext(options);
        var provider = Provider(read);

        Assert.Equal(DayOfWeek.Friday, await provider.GetMarketDayAsync(new DateOnly(2026, 8, 14)));
        Assert.Equal(DayOfWeek.Thursday, await provider.GetMarketDayAsync(new DateOnly(2026, 8, 27)));
        Assert.Equal(DayOfWeek.Saturday, await provider.GetMarketDayAsync(new DateOnly(2026, 9, 5)));
    }
}
