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

        var overview = await new DashboardRepository(context, new FacilityReportsRepository(context)).GetOverviewAsync(2026, 6, CancellationToken.None);

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

    [Fact]
    public async Task GetOverview_FacilityCards_ReflectEachFacilitysOwnContext()
    {
        var context = NewContext();
        var year = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Year;
        var month = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Month;
        var monthDate = new DateOnly(year, month, 1);

        // NPM: a stall with a paid daily collection (₱30) — no monthly rent record this month.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var npmStall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental);
        var npmContract = Contract.Create(npmStall.Id, "Ana Reyes", null, new DateOnly(2025, 1, 1), 3, 900m);
        var daily = EEMOCantilanSDS.Domain.Entities.Payments.DailyCollection.Create(npmStall.Id, monthDate);
        daily.MarkPaid("OR-D1", null);

        // SLH: one hog slaughter (₱250).
        var slh = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH");
        var slaughter = EEMOCantilanSDS.Domain.Entities.Slaughterhouse.SlaughterTransaction
            .CreateHog(slh.Id, null, "Pedro", 1, "OR-S1", monthDate);

        // TRM: one trip (₱30).
        var trm = Facility.Create(FacilityCode.TRM, "Transport Terminal", "TRM");
        var transporter = EEMOCantilanSDS.Domain.Entities.TransportTerminal.TrmTransporter.Create("Driver A", "Org", "Route", "ABC123");
        var trip = EEMOCantilanSDS.Domain.Entities.TransportTerminal.TrmTrip.Create(transporter.Id, 1, "Driver A", "ABC123", "Route", "OR-T1");

        // TPM: one paid vendor attendance (₱100) on a Friday of the month.
        var tpm = Facility.Create(FacilityCode.TPM, "Tabo-an Public Market", "TPM");
        var vendor = EEMOCantilanSDS.Domain.Entities.TaboanMarket.TpmVendor.Create("Vendor V", "Vegetables");
        var friday = monthDate;
        while (friday.DayOfWeek != DayOfWeek.Friday) friday = friday.AddDays(1);
        var attendance = EEMOCantilanSDS.Domain.Entities.TaboanMarket.TpmAttendance.Create(vendor.Id, friday);
        attendance.MarkPaid(null);

        context.AddRange(npm, npmStall, npmContract, daily, slh, slaughter, trm, transporter, trip, tpm, vendor, attendance);
        await context.SaveChangesAsync();

        var overview = await new DashboardRepository(context, new FacilityReportsRepository(context)).GetOverviewAsync(year, month, CancellationToken.None);

        Assert.Equal(30m, overview.Facilities.Single(f => f.Code == FacilityCode.NPM).Collected);   // daily collection
        Assert.Equal(250m, overview.Facilities.Single(f => f.Code == FacilityCode.SLH).Collected);  // slaughter
        Assert.Equal(30m, overview.Facilities.Single(f => f.Code == FacilityCode.TRM).Collected);   // trip
        Assert.Equal(100m, overview.Facilities.Single(f => f.Code == FacilityCode.TPM).Collected);  // market day

        // Total includes every facility's own-context revenue.
        Assert.Equal(410m, overview.TotalCollected);
    }
}
