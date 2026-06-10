using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Regression tests for the server-aggregated TRM collection history
/// (<see cref="TrmRepository.GetHistoryAsync"/>), which powers the report's Yearly and History phases.
/// Trips are stamped with the current UTC time by the domain factory, so the aggregation is verified
/// against the current year (past-year seeding isn't possible through the factory).
/// </summary>
public class TrmHistoryTests : RepositoryTestBase
{
    [Fact]
    public async Task GetHistory_AggregatesTripsByOrganizationTransporterAndFee()
    {
        var context = NewContext();
        var year = PhilippineTime.Today.Year;

        var orgX1 = TrmTransporter.Create("Driver A", "Org X", "Route 1", "ABC123");
        var orgX2 = TrmTransporter.Create("Driver B", "Org X", "Route 2", "DEF456");
        var orgY1 = TrmTransporter.Create("Driver C", "Org Y", "Route 3", "GHI789");
        context.TrmTransporters.AddRange(orgX1, orgX2, orgY1);

        // 3 trips: 2 under Org X (2 distinct transporters), 1 under Org Y. Each trip is ₱30.
        // Routes: "North" x2, "South" x1. Trips carry their own organization (as the handler stamps it).
        context.TrmTrips.AddRange(
            TrmTrip.Create(orgX1.Id, 1, "Driver A", "ABC123", "North", "OR-1", organization: "Org X"),
            TrmTrip.Create(orgX2.Id, 2, "Driver B", "DEF456", "North", "OR-2", organization: "Org X"),
            TrmTrip.Create(orgY1.Id, 3, "Driver C", "GHI789", "South", "OR-3", organization: "Org Y"));
        await context.SaveChangesAsync();

        var repo = new TrmRepository(context);
        var history = await repo.GetHistoryAsync(year, CancellationToken.None);

        Assert.Equal(year, history.Year);
        Assert.Equal(5, history.Yearly.Count);   // rolling 5-year window

        var thisYear = history.Yearly.Single(y => y.Year == year);
        Assert.Equal(3, thisYear.Trips);
        Assert.Equal(3, thisYear.Transporters);          // 3 distinct transporters
        Assert.Equal(3 * FeeRates.TrmTripFee, thisYear.Collected);

        // Organization tally: Org X = 2 trips, Org Y = 1 trip (ordered by trips desc).
        Assert.Equal("Org X", thisYear.Organizations[0].Organization);
        Assert.Equal(2, thisYear.Organizations[0].Trips);
        Assert.Equal(2 * FeeRates.TrmTripFee, thisYear.Organizations[0].Collected);
        Assert.Contains(thisYear.Organizations, o => o.Organization == "Org Y" && o.Trips == 1);

        // Route tally: North = 2 trips, South = 1 trip (ordered by trips desc).
        Assert.Equal("North", thisYear.Routes[0].Route);
        Assert.Equal(2, thisYear.Routes[0].Trips);
        Assert.Equal(2 * FeeRates.TrmTripFee, thisYear.Routes[0].Collected);
        Assert.Contains(thisYear.Routes, r => r.Route == "South" && r.Trips == 1);

        // Trips all landed in the current month → that month's row mirrors the year totals.
        var monthRow = history.Monthly.Single(m => m.Month == PhilippineTime.Today.Month);
        Assert.Equal(3, monthRow.Trips);
        Assert.Equal(3 * FeeRates.TrmTripFee, monthRow.Collected);
    }

    [Fact]
    public async Task GetHistory_FutureYear_ReturnsNoMonthlyRows()
    {
        var context = NewContext();
        var repo = new TrmRepository(context);

        var history = await repo.GetHistoryAsync(2999, CancellationToken.None);

        Assert.Empty(history.Monthly);
        Assert.Equal(5, history.Yearly.Count);
        Assert.All(history.Yearly, y => Assert.Equal(0, y.Trips));
    }
}
