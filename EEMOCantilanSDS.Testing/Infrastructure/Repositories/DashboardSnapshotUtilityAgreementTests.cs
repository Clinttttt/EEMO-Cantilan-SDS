using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The dashboard hero and the Financial Report state the same period's money, and they must not answer differently.
///
/// <para>
/// They did. On September 2026 the dashboard read ₱12,180 unpaid and the report read ₱12,192 — one unpaid ₱12
/// electricity charge on stall 1 of the market. The report has folded the market's metered utilities into NPM's
/// collected and unpaid since they were normalised; the dashboard's snapshot did not. These tests hold the snapshot to
/// the same rule, measured against the utility totals themselves rather than against a copied figure.
/// </para>
/// </summary>
public class DashboardSnapshotUtilityAgreementTests : RepositoryTestBase
{
    private const int Year = 2026;
    private const int Month = 6;

    private static (Facility f, Stall s, Contract c) MarketSpace(string stallNo, string occupant)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, stallNo, 900m, ApplicableFees.BaseRental, MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, occupant, occupant, new DateOnly(Year, 1, 1), 3, 900m);
        return (facility, stall, contract);
    }

    [Fact]
    public async Task AnUnpaidUtilityCharge_IsInTheSnapshotsPending()
    {
        // The exact shape of the September disagreement: a charge raised, nothing collected against it.
        var context = NewContext();
        var (facility, stall, contract) = MarketSpace("1", "Karmilita Log");
        context.AddRange(facility, stall, contract);

        // 1 kWh at ₱12 = ₱12 of electricity, unpaid. No water reading, so nothing to charge for water.
        var bill = UtilityBill.Create(stall.Id, Year, Month, 0m, 1m, 12m, 0m, 0m, 25m, "seed");
        bill.RecordPayment(null, null, null, PaymentStatus.Unpaid, 0m, PaymentStatus.Unpaid, 0m, null, "seed");
        context.Add(bill);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);

        var totals = await repo.GetNpmUtilityTotalsAsync(Year, Month, CancellationToken.None);
        var snapshot = await repo.GetFacilitySnapshotAsync(FacilityCode.NPM, facility.Id, Year, Month, CancellationToken.None);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, Year, Month, null, CancellationToken.None);

        Assert.Equal(12m, totals.Outstanding);

        // What the Financial Report states as NPM's unpaid: the stall balance plus the utility outstanding. The
        // snapshot the dashboard reads must come to the same figure.
        Assert.Equal(report.PendingPaymentAmount + totals.Outstanding, snapshot.Pending);

        // And it must not be the stall balance alone, which is what the dashboard was showing.
        Assert.NotEqual(report.PendingPaymentAmount, snapshot.Pending);
    }

    [Fact]
    public async Task ACollectedUtilityCharge_IsInTheSnapshotsCollected()
    {
        // The other half, and the one nobody would have noticed on screen: in September the market had collected no
        // electricity or water at all, so only the unpaid side disagreed. A month where it collects some would have put
        // the dashboard's Total Collected under the report's by the same amount.
        var context = NewContext();
        var (facility, stall, contract) = MarketSpace("2", "Kim Chui");
        context.AddRange(facility, stall, contract);

        // ₱100 of electricity settled in full, ₱40 of water part-settled at ₱25.
        var bill = UtilityBill.Create(stall.Id, Year, Month, 0m, 10m, 10m, 0m, 2m, 20m, "seed");
        bill.RecordPayment("OR-1", null, null, PaymentStatus.Paid, 0m, PaymentStatus.Partial, 25m, null, "seed");
        context.Add(bill);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);

        var totals = await repo.GetNpmUtilityTotalsAsync(Year, Month, CancellationToken.None);
        var snapshot = await repo.GetFacilitySnapshotAsync(FacilityCode.NPM, facility.Id, Year, Month, CancellationToken.None);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, Year, Month, null, CancellationToken.None);

        Assert.Equal(125m, totals.ElecCollected + totals.WaterCollected);   // 100 + 25
        Assert.Equal(15m, totals.Outstanding);                              // the 40 - 25 still due on water

        Assert.Equal(report.TotalRevenue + totals.ElecCollected + totals.WaterCollected, snapshot.Collected);
        Assert.Equal(report.PendingPaymentAmount + totals.Outstanding, snapshot.Pending);
    }

    [Fact]
    public async Task AFacilityWithNoMeters_IsUnaffected()
    {
        // Only the market is metered. A monthly-billed facility must not acquire a utility figure, and must not pay for
        // a query asking about one either.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Rosa Magbanua", "Rosa Magbanua", new DateOnly(Year, 1, 1), 3, 900m);
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);

        var snapshot = await repo.GetFacilitySnapshotAsync(FacilityCode.TCC, facility.Id, Year, Month, CancellationToken.None);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Monthly, Year, Month, null, CancellationToken.None);

        Assert.Equal(report.TotalRevenue, snapshot.Collected);
        Assert.Equal(report.PendingPaymentAmount, snapshot.Pending);
    }
}
