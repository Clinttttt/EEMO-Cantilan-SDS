using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The shared delinquency source (dashboard + Financial Reports): counts unpaid/partial billing months
/// over the rolling 12-month window EXCLUDING the current month, sums their balance due (cumulative),
/// and can be scoped to one facility.
/// </summary>
public class FacilityReportsDelinquencyTests : RepositoryTestBase
{
    [Fact]
    public async Task GetDelinquentStalls_SumsPastUnpaidMonths_ExcludesCurrent_AndScopesByFacility()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var m1 = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var m2 = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);

        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(tcc.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Behind Tenant", "Behind Tenant", new DateOnly(today.Year, 1, 1), 3, 1000m);
        // Two past unpaid months (₱1,000 each) + the current month unpaid (must be excluded).
        var past1 = PaymentRecord.Create(stall.Id, m1.Year, m1.Month, 1000m);
        var past2 = PaymentRecord.Create(stall.Id, m2.Year, m2.Month, 1000m);
        var current = PaymentRecord.Create(stall.Id, today.Year, today.Month, 1000m);

        context.AddRange(tcc, stall, contract, past1, past2, current);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);

        var all = await repo.GetDelinquentStallsAsync(null, today.Year, today.Month, CancellationToken.None);

        var row = Assert.Single(all);
        Assert.Equal(FacilityCode.TCC, row.FacilityCode);
        Assert.Equal("101", row.StallNo);
        Assert.Equal("Behind Tenant", row.Occupant);
        Assert.Equal(2, row.MonthsUnpaid);             // two past months; the current month is excluded (not 3)
        Assert.Equal(2_000m, row.OutstandingBalance);  // cumulative balance across the two months

        // Scoped to a different facility → none.
        var ncc = await repo.GetDelinquentStallsAsync(FacilityCode.NCC, today.Year, today.Month, CancellationToken.None);
        Assert.Empty(ncc);
    }
}
