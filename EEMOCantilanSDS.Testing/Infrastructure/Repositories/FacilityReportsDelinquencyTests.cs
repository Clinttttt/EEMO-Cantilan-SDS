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

        // The stall was let from 1 January and nothing was ever recorded as paid, so every elapsed month of this
        // year is owed — not only the two that happen to carry an Unpaid record. Counting records was the old rule
        // and it under-reported: five of these months have no row at all, because nothing writes one until money
        // is recorded. This is the same figure the stall's own compliance row shows.
        var elapsedMonths = today.Month - 1;

        var row = Assert.Single(all);
        Assert.Equal(FacilityCode.TCC, row.FacilityCode);
        Assert.Equal("101", row.StallNo);
        Assert.Equal("Behind Tenant", row.Occupant);
        Assert.Equal(elapsedMonths, row.MonthsUnpaid);              // the current month is still excluded
        Assert.Equal(elapsedMonths * 1_000m, row.OutstandingBalance);

        // Scoped to a different facility → none.
        var ncc = await repo.GetDelinquentStallsAsync(FacilityCode.NCC, today.Year, today.Month, CancellationToken.None);
        Assert.Empty(ncc);
    }

    [Fact]
    public async Task GetDelinquentStalls_LeavesOutAClosedStall_WhoseDebtTheClosedRegisterReports()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var m1 = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var m2 = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);

        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(tcc.Id, "202", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Closed Tenant", "Closed Tenant", new DateOnly(today.Year, 1, 1), 3, 1000m);
        stall.Close(m1);
        var past1 = PaymentRecord.Create(stall.Id, m1.Year, m1.Month, 1000m);
        var past2 = PaymentRecord.Create(stall.Id, m2.Year, m2.Month, 1000m);

        context.AddRange(tcc, stall, contract, past1, past2);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);

        // Freezing a stall ends its obligation — the platform's rule everywhere, which is why a closed stall's own
        // compliance row reports no missed months either. What the account still owes is stated by the Closed /
        // Inactive Accounts register, so this count leaves it out whether or not the caller opts in.
        Assert.Empty(await repo.GetDelinquentStallsAsync(null, today.Year, today.Month, CancellationToken.None));
        Assert.Empty(await repo.GetDelinquentStallsAsync(null, today.Year, today.Month, includeClosed: true, CancellationToken.None));
    }
}
