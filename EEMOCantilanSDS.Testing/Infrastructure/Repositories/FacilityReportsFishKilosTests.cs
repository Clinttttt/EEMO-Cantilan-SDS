using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Fish kilos on the market's own report: what each space weighed over the month.
///
/// <para>
/// The office weighs fish per day and the sheet has to state the month's kilos per vendor, so the figure is summed
/// per stall alongside the money. The care here is the stall that settled its month with a single payment: the
/// dictionary this repository already builds from daily collections deliberately EXCLUDES those stalls, because for
/// MONEY their daily rows must not be counted twice. Kilos are not money. They are recorded on the daily row whichever
/// way the stall paid, so summing them from that same dictionary would report nothing for a vendor who paid monthly
/// and weighed fish all month.
/// </para>
/// </summary>
public class FacilityReportsFishKilosTests : RepositoryTestBase
{
    private static (Facility f, Stall s, Contract c) FishStall(string stallNo, string occupant)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, stallNo, 900m, ApplicableFees.BaseRental, MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, occupant, occupant, new DateOnly(2026, 1, 1), 3, 900m);
        return (facility, stall, contract);
    }

    private static DailyCollection Weighed(Guid stallId, int day, decimal kilos)
    {
        var dc = DailyCollection.Create(stallId, new DateOnly(2026, 6, day), "seed", 30m);
        dc.MarkPaid(orNumber: $"OR-{day}", collectorId: null, fishKilos: kilos, updatedBy: "seed");
        return dc;
    }

    [Fact]
    public async Task TheMonthsKilosAreSummedPerSpace()
    {
        var context = NewContext();
        var (facility, stall, contract) = FishStall("1", "Kim Chui");
        context.AddRange(facility, stall, contract);
        context.AddRange(Weighed(stall.Id, 2, 12.5m), Weighed(stall.Id, 3, 7m), Weighed(stall.Id, 4, 0.5m));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(20m, c.FishKilos);
    }

    [Fact]
    public async Task AVendorWhoSettledTheWholeMonthAtOnce_StillHasItsKilosCounted()
    {
        // The case the money rule would have hidden. This stall carries a monthly payment record for June AND weighed
        // fish on three days; the kilos belong on the sheet either way.
        var context = NewContext();
        var (facility, stall, contract) = FishStall("2", "Justin Bieber");
        context.AddRange(facility, stall, contract);

        var paid = PaymentRecord.Create(stall.Id, 2026, 6, 900m, "seed");
        paid.UpdateStatus(PaymentStatus.Paid, 900m, "OR-MONTH", "seed", null);
        context.Add(paid);
        context.AddRange(Weighed(stall.Id, 5, 9m), Weighed(stall.Id, 6, 6m));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(15m, c.FishKilos);
    }

    [Fact]
    public async Task ASpaceThatWeighedNothing_ReportsNoKilos()
    {
        var context = NewContext();
        var (facility, stall, contract) = FishStall("3", "Karmilita Log");
        context.AddRange(facility, stall, contract);
        var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 9), "seed", 30m);
        dc.MarkPaid(orNumber: "OR-9", collectorId: null, fishKilos: null, updatedBy: "seed");
        context.Add(dc);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(0m, c.FishKilos);
    }

    [Fact]
    public async Task KilosFromAnotherMonth_StayOutOfThisOne()
    {
        var context = NewContext();
        var (facility, stall, contract) = FishStall("4", "Rosa Magbanua");
        context.AddRange(facility, stall, contract);

        var may = DailyCollection.Create(stall.Id, new DateOnly(2026, 5, 20), "seed", 30m);
        may.MarkPaid(orNumber: "OR-MAY", collectorId: null, fishKilos: 40m, updatedBy: "seed");
        context.Add(may);
        context.Add(Weighed(stall.Id, 10, 3m));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        var c = Assert.Single(report.StallCompliance);
        Assert.Equal(3m, c.FishKilos);
    }
}
