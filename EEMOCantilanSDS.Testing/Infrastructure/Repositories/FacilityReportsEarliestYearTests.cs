using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The Follow-up History year picker floors at the earliest year with data, so a back-dated prior-year
/// settlement is reachable. Locks that <see cref="FacilityReportsRepository.GetEarliestActivityYearAsync"/>
/// returns the MIN across daily collections, contracts and monthly billing (and the current year when empty).
/// </summary>
public class FacilityReportsEarliestYearTests : RepositoryTestBase
{
    [Fact]
    public async Task EarliestActivityYear_IsMinAcrossDailyContractAndBilling()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, "Ramil C. Orjeles", "Ramil C. Orjeles", new DateOnly(2023, 6, 7), 3, 900m);

        // A daily collection fee-dated 2022 — the earliest signal.
        var dc = DailyCollection.Create(stall.Id, new DateOnly(2022, 5, 1));
        dc.MarkPaid("OR-1", null);

        context.AddRange(facility, stall, contract, dc);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var earliest = await repo.GetEarliestActivityYearAsync(CancellationToken.None);

        Assert.Equal(2022, earliest);
    }

    [Fact]
    public async Task EarliestActivityYear_NoData_ReturnsCurrentYear()
    {
        var context = NewContext();
        var repo = new FacilityReportsRepository(context);

        var earliest = await repo.GetEarliestActivityYearAsync(CancellationToken.None);

        Assert.Equal(EEMOCantilanSDS.Domain.Common.PhilippineTime.Today.Year, earliest);
    }
}
