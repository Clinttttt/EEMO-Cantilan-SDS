using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Time;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// The roster states each stall at the fee of ITS OWN area, where an office prices the areas of its market apart.
///
/// This is the repository half of the office's request from 2026-08-23. The rule lives in NpmDailyFee; a report is where
/// the office reads the consequence, so it is asserted through the real repository against seeded rate rows rather than
/// against the rule in isolation. An office that states one rate for its market is asserted unchanged in the same suite,
/// because that is the case every existing figure depends on.
/// </summary>
public class StallHoldersListPerAreaRateTests : RepositoryTestBase
{
    private static readonly DateOnly RateEffective = new(2020, 1, 1);

    private static (Stall Stall, Contract Contract) StallIn(Guid facilityId, string stallNo, MarketSection section)
    {
        var stall = Stall.Create(facilityId, stallNo, 900m, ApplicableFees.DailyRental, section: section);
        var contract = Contract.Create(
            stall.Id, "Diego Brando", "Diego Brando", PhilippineTime.Today.AddMonths(-1), 3, 900m);
        return (stall, contract);
    }

    [Fact]
    public async Task EachAreaIsStatedAtTheFeeTheOfficeSetForIt()
    {
        // Vegetables ₱35, fish left at the market's ₱30 — the shape the office described.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "Public Market", "NPM");
        var (veg, vegContract) = StallIn(facility.Id, "1", MarketSection.VegetableArea);
        var (fish, fishContract) = StallIn(facility.Id, "2", MarketSection.FishSection);

        context.AddRange(
            facility, veg, vegContract, fish, fishContract,
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, RateEffective, Guid.Empty),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStallVegetable, 35m, RateEffective, Guid.Empty));
        await context.SaveChangesAsync();

        var dto = await new StallRepository(context)
            .GetStallHoldersListAsync(FacilityCode.NPM, null, null, CancellationToken.None);

        var rows = dto.Sections.SelectMany(s => s.Rows).ToDictionary(r => r.StallNo, r => r.MonthlyRentalRate);

        Assert.Equal(1_050m, rows["1"]);   // ₱35 × 30, the vegetable area's own rate
        Assert.Equal(900m, rows["2"]);     // ₱30 × 30, the market's rate for an area priced no differently
    }

    [Fact]
    public async Task AnOfficeWithOneMarketRateIsUnchanged()
    {
        // The reference case. Every area reads the market's rate, which is what the roster showed before per-area rates
        // existed — so this figure moving would be a regression for Cantilan, not a feature.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "Public Market", "NPM");
        var (veg, vegContract) = StallIn(facility.Id, "1", MarketSection.VegetableArea);
        var (meat, meatContract) = StallIn(facility.Id, "2", MarketSection.MeatSection);

        context.AddRange(
            facility, veg, vegContract, meat, meatContract,
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, RateEffective, Guid.Empty));
        await context.SaveChangesAsync();

        var dto = await new StallRepository(context)
            .GetStallHoldersListAsync(FacilityCode.NPM, null, null, CancellationToken.None);

        foreach (var row in dto.Sections.SelectMany(s => s.Rows))
            Assert.Equal(900m, row.MonthlyRentalRate);
    }

    [Fact]
    public async Task AStatedMonthlyRentStillWins_EvenWhereAnAreaIsPricedApart()
    {
        // The office's stated month is its ordinance and outranks any arithmetic: ₱1,000 is what a month owes, whatever
        // the area's daily fee happens to multiply to.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "Public Market", "NPM");
        var (veg, vegContract) = StallIn(facility.Id, "1", MarketSection.VegetableArea);

        context.AddRange(
            facility, veg, vegContract,
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, RateEffective, Guid.Empty),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStallVegetable, 35m, RateEffective, Guid.Empty),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 1_000m, RateEffective, Guid.Empty));
        await context.SaveChangesAsync();

        var dto = await new StallRepository(context)
            .GetStallHoldersListAsync(FacilityCode.NPM, null, null, CancellationToken.None);

        var row = Assert.Single(Assert.Single(dto.Sections).Rows);
        Assert.Equal(1_000m, row.MonthlyRentalRate);
    }
}
