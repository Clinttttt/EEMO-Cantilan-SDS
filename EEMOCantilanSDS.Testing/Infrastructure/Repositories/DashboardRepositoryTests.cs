using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

public class DashboardRepositoryTests : RepositoryTestBase
{
    [Fact]
    public async Task GetOverview_ReportsRealCollected_Unpaid_Recent_AndDelinquents()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");

        var paidStall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental);
        var owingStall = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental);
        var paidContract = Contract.Create(paidStall.Id, "Juan Dela Cruz", null, new DateOnly(2025, 1, 1), 3, 900m);
        var owingContract = Contract.Create(owingStall.Id, "Maria Santos", null, new DateOnly(2025, 1, 1), 3, 900m);

        var junePaid = PaymentRecord.Create(paidStall.Id, 2026, 6, 900m);
        junePaid.UpdateStatus(PaymentStatus.Paid);                 // collected this month
        var mayUnpaid = PaymentRecord.Create(owingStall.Id, 2026, 5, 900m); // recorded, unpaid -> delinquent

        context.AddRange(facility, paidStall, owingStall, paidContract, owingContract, junePaid, mayUnpaid);
        await context.SaveChangesAsync();

        var overview = await new DashboardRepository(context).GetOverviewAsync(2026, 6, CancellationToken.None);

        Assert.Equal(900m, overview.TotalCollected);
        Assert.Equal(1, overview.PaidCount);

        var npm = Assert.Single(overview.Facilities, f => f.Code == FacilityCode.NPM);
        Assert.Equal(900m, npm.Collected);
        Assert.Equal(2, npm.TotalVendors);
        Assert.Equal(1, npm.UnpaidCount);
        Assert.Equal(50, npm.CollectionRate);

        var tx = Assert.Single(overview.RecentTransactions);
        Assert.Equal("Juan Dela Cruz", tx.PayorName);
        Assert.Equal(900m, tx.Amount);

        var delinquent = Assert.Single(overview.DelinquentVendors);
        Assert.Equal("Maria Santos", delinquent.Name);
        Assert.Equal(1, delinquent.MonthsUnpaid);
    }
}
