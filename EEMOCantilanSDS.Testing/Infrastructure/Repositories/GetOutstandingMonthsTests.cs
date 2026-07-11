using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The Pay-bill form's source of truth. Regression: an NPM stall with NO daily collections at all
/// must still surface its unpaid months (each with balance) across the whole contract — the earlier
/// payment-history source omitted no-collection months, so a ₱30k+ account wrongly read "fully paid".
/// </summary>
public class GetOutstandingMonthsTests : RepositoryTestBase
{
    [Fact]
    public async Task Npm_NoCollections_ReturnsUnpaidMonths_NotEmpty()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var startMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Vendor", "Vendor", startMonth, 3, 900m);   // active, no daily collections
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var months = await repo.GetOutstandingMonthsAsync(stall.Id, CancellationToken.None);

        Assert.NotEmpty(months);                                    // was: empty → "fully paid" bug
        Assert.All(months, m => Assert.True(m.BalanceDue > 0m));

        // A full past month owes daysInMonth × ₱30.
        var fullMonth = months.First(m => m.Period == $"{startMonth.Year:0000}-{startMonth.Month:00}");
        Assert.Equal(DateTime.DaysInMonth(startMonth.Year, startMonth.Month) * FeeRates.NpmDailyFee, fullMonth.BalanceDue);
    }

    [Fact]
    public async Task Monthly_UnpaidMonth_ReturnsRentOwed()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var startMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Tenant", "Tenant", startMonth, 3, 1000m);   // unpaid, no records
        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var months = await repo.GetOutstandingMonthsAsync(stall.Id, CancellationToken.None);

        Assert.NotEmpty(months);
        Assert.All(months, m => Assert.Equal(1000m, m.BalanceDue));
    }
}
