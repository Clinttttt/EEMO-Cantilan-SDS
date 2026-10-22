using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The inactive register's historical (as-of / period) selection and its end-date boundary. An occupancy's End is the LAST day the
/// lessee held the stall, inclusive: on a past as-of date that day is over, so an occupancy ending ON the cutoff has ended and is
/// listed; one ending the day after is not; and on the real current day an occupancy ending today is still in force.
/// </summary>
public class ClosedStallAccountsAsOfBoundaryTests : RepositoryTestBase
{
    private static (Facility Facility, Stall Stall, Contract Term) NpmStall(DateOnly from, DateOnly? terminatedOn)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var term = Contract.Create(stall.Id, "Ramil C. Orjeles", "Ramil C. Orjeles", from, 1, 900m);
        if (terminatedOn is { } ended) term.Terminate("Head", ended);
        return (facility, stall, term);
    }

    private static async Task<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Stalls.ClosedStallAccountDto>> AsOf(
        EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext context, DateOnly asOf) =>
        await new StallRepository(context).GetClosedStallAccountsAsOfAsync(asOf, CancellationToken.None);

    [Fact]
    public async Task TerminationExactlyOnTheCutoff_IsListed_ByBothThePeriodAndTheAsOfReader_WithTheOriginalAnnualObligation()
    {
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        var period = Assert.Single(await new StallRepository(context).GetClosedStallAccountsForPeriodAsync(
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), CancellationToken.None));
        Assert.Equal(10_800m, period.Uncollected);                                      // twelve months of ₱900, unchanged

        var asOf = Assert.Single(await AsOf(context, new DateOnly(2025, 12, 31)));
        Assert.Equal(10_800m, asOf.Uncollected);
        Assert.Equal(new DateOnly(2025, 12, 31), asOf.OccupancyEndedOn);
    }

    [Theory]
    [InlineData(2025, 12, 30, true)]     // ended the day before the cutoff
    [InlineData(2025, 12, 31, true)]     // ended on the cutoff (its last day is over by then)
    [InlineData(2026, 1, 1, false)]      // ended the day after: still held on the cutoff
    [InlineData(2026, 3, 15, false)]     // ended long after
    public async Task AnOccupancy_IsEndedAsOfTheCutoffOnlyWhenItsLastDayIsOnOrBeforeIt(int year, int month, int day, bool listed)
    {
        var context = NewContext();
        var (facility, stall, term) = NpmStall(new DateOnly(2025, 1, 1), new DateOnly(year, month, day));
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        var rows = await AsOf(context, new DateOnly(2025, 12, 31));
        Assert.Equal(listed ? 1 : 0, rows.Count);
    }

    [Fact]
    public async Task AHandover_ListsTheOutgoingLesseeFromItsLastDay_NeverBefore_AndNeverTheIncomingOne()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "3", 1_000m, ApplicableFees.BaseRental);
        var outgoing = Contract.Create(stall.Id, "Wilma K. Tecson", "Wilma K. Tecson", new DateOnly(2026, 1, 1), 1, 1_000m);
        outgoing.Terminate("Head", new DateOnly(2026, 6, 15));
        var incoming = Contract.Create(stall.Id, "Teofila Reyes", "Teofila Reyes", new DateOnly(2026, 6, 16), 3, 1_000m);
        context.AddRange(facility, stall, outgoing, incoming);
        await context.SaveChangesAsync();

        Assert.Empty(await AsOf(context, new DateOnly(2026, 6, 14)));                    // Wilma still held it
        var onHerLastDay = Assert.Single(await AsOf(context, new DateOnly(2026, 6, 15)));
        Assert.Equal("Wilma K. Tecson", onHerLastDay.Occupant);
        var afterwards = Assert.Single(await AsOf(context, new DateOnly(2026, 7, 31)));
        Assert.Equal("Wilma K. Tecson", afterwards.Occupant);                           // Teofila holds the stall: never an inactive account
        Assert.Equal(5_000m, afterwards.Uncollected);                                    // the handover month stays with the later lessee
    }

    [Fact]
    public async Task AnOccupancyEndingOnTheRealCurrentDay_IsStillInForce_ForAnAsOfTodayReading()
    {
        var today = PhilippineTime.Today;
        var context = NewContext();
        var (facility, stall, term) = NpmStall(today.AddMonths(-3), today);
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        Assert.Empty(await AsOf(context, today));                                        // today is not over, so its last day has not passed
        Assert.Empty(await AsOf(context, today.AddDays(1)));                            // a future as-of date is capped to today and reads the same position
    }

    [Fact]
    public async Task AClosureDatedAfterTheCutoff_IsNotAnEndedAccountOnThatDate()
    {
        var today = PhilippineTime.Today;
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "9", 1_000m, ApplicableFees.BaseRental);
        var term = Contract.Create(stall.Id, "Open Lessee", "Open Lessee", today.AddMonths(-6), 3, 1_000m);
        stall.Close(today.AddDays(10));                                                  // a future closure
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        Assert.Empty(await AsOf(context, today));                                        // not closed yet on this date
        Assert.Empty(await AsOf(context, today.AddMonths(-1)));
    }
}
