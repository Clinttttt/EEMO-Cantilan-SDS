using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Services;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// A month's market days are the dates the OFFICE's schedule states, and the overview must carry them.
///
/// <para>
/// The portal's Tabo-an calendar used to be handed a single weekday and expand it across the month itself. That
/// silently re-answered weeks that had already been held: an office moving its market from Friday to Thursday from
/// the 27th had its earlier Fridays relabelled as Thursdays — dates the market never opened on — while the Fridays
/// actually collected vanished from the list. The count was already right, which is what made it easy to miss: only
/// the dates were wrong, and only on screen.
/// </para>
///
/// <para>
/// So the contract these tests hold is the DTO's: the month's dates travel from the schedule to the portal, and a
/// month the day moved in carries both weekdays. A caller cannot rebuild them from <c>MarketDay</c> alone, which is
/// why that property now says so in its own remarks.
/// </para>
/// </summary>
public class TpmOverviewMarketDatesTests : RepositoryTestBase
{
    private const string TenantCode = "madrid";

    private sealed class Tenant : ITenantContext
    {
        public string? TenantCode => TpmOverviewMarketDatesTests.TenantCode;
        public void SetTenantCode(string? tenantCode) { }
    }

    /// <summary>August 2026: Fridays fall on the 7th, 14th, 21st and 28th; Thursdays on the 6th, 13th, 20th and 27th.</summary>
    private const int Year = 2026;
    private const int August = 8;

    /// <summary>
    /// An office whose registry record says which day it holds its market, so a month with no schedule row still
    /// answers from the office's own arrangement rather than from the platform's default.
    /// </summary>
    private static async Task<(TpmRepository repo, Guid municipalityId)> BuildAsync(AppDbContext context, DayOfWeek registered)
    {
        var municipality = Municipality.Create(
            "MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: TenantCode, tpmMarketDay: registered);
        context.Municipalities.Add(municipality);
        await context.SaveChangesAsync();

        return (new TpmRepository(context, new TpmMarketDayProvider(context, new Tenant())), municipality.Id);
    }

    [Fact]
    public async Task AMonthTheDayMovedIn_CarriesBothWeekdays_InDateOrder()
    {
        using var context = NewContext();
        var (repo, municipalityId) = await BuildAsync(context, DayOfWeek.Friday);

        // How the office's own move is recorded: a baseline for the day it was held on, then the new day from the
        // date it starts. This is exactly what SetTpmMarketDayCommandHandler writes.
        context.TpmMarketDaySchedules.Add(Schedule(municipalityId, DayOfWeek.Friday, DateOnly.MinValue));
        context.TpmMarketDaySchedules.Add(Schedule(municipalityId, DayOfWeek.Thursday, new DateOnly(Year, August, 27)));
        await context.SaveChangesAsync();

        var overview = await repo.GetOverviewAsync(Year, August);

        // The three Fridays already held stay Fridays. The 27th onwards is the new arrangement. The 28th is NOT a
        // market day any more, even though it is a Friday, because by then the market had moved.
        Assert.Equal(
            new[]
            {
                new DateOnly(Year, August, 7),
                new DateOnly(Year, August, 14),
                new DateOnly(Year, August, 21),
                new DateOnly(Year, August, 27),
            },
            overview.MarketDates);

        // The count the office reconciles by hand agrees with the dates it can see.
        Assert.Equal(overview.MarketDates.Count, overview.FridaysThisMonth);
    }

    [Fact]
    public async Task AMonthWithNoMove_IsEveryOccurrenceOfTheOfficesOwnDay()
    {
        using var context = NewContext();
        var (repo, municipalityId) = await BuildAsync(context, DayOfWeek.Thursday);

        context.TpmMarketDaySchedules.Add(Schedule(municipalityId, DayOfWeek.Thursday, DateOnly.MinValue));
        await context.SaveChangesAsync();

        var overview = await repo.GetOverviewAsync(Year, August);

        Assert.Equal(
            new[]
            {
                new DateOnly(Year, August, 6),
                new DateOnly(Year, August, 13),
                new DateOnly(Year, August, 20),
                new DateOnly(Year, August, 27),
            },
            overview.MarketDates);
        Assert.Equal(DayOfWeek.Thursday, overview.MarketDay);
    }

    [Fact]
    public async Task TheDatesAreNeverEmpty_SoTheCalendarNeverFallsBackToGuessing()
    {
        // An office that has never moved its day has no schedule rows at all: the dates then come from its own
        // registry record, or from the platform's Friday when even that is unset. Either way the month has dates,
        // because a calendar with none would send the portal back to expanding one weekday itself.
        using var context = NewContext();
        var (repo, _) = await BuildAsync(context, DayOfWeek.Friday);

        var overview = await repo.GetOverviewAsync(Year, August);

        Assert.NotEmpty(overview.MarketDates);
        Assert.All(overview.MarketDates, d => Assert.Equal(overview.MarketDay, d.DayOfWeek));
    }

    private static TpmMarketDaySchedule Schedule(Guid municipalityId, DayOfWeek day, DateOnly from)
    {
        return TpmMarketDaySchedule.Create(day, from, municipalityId, createdBy: "test");
    }
}
