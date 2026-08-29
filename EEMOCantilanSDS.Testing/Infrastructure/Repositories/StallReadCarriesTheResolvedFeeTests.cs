using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// What a stall read carries about the fee that stall is billed.
///
/// <para>
/// Three screens stated a market stall's daily fee from the MARKET's rate: the stall profile's Rate field, the vendor
/// detail card, and Collection Exceptions' expected total, which multiplies days by it. Correct for an office that prices
/// its whole market at one figure; wrong the moment it prices an area, or one of its own sections, apart — the office would
/// read one figure on a stall's own profile and be charged another by its collector.
/// </para>
///
/// <para>
/// So the read now carries BOTH: <c>DailyRate</c>, the rate the space was let at as recorded on the stall, which is what
/// the forms that EDIT a stall must show; and <c>ResolvedDailyFee</c>, what the stall is billed, settled by the one rule.
/// Keeping them apart is the point: a form pre-filled with a resolved figure would stamp it as the stall's own rate, and an
/// own rate outranks its section's for ever.
/// </para>
/// </summary>
public class StallReadCarriesTheResolvedFeeTests
{
    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    /// <summary>Seeds a market with one stall in a section of the office's own naming, and the rates given.</summary>
    private static async Task<DbContextOptions<AppDbContext>> SeedAsync(
        decimal marketRate, decimal? sectionRate, decimal? stallOwnRate, string section = "Sari-sari Area")
    {
        var options = Options();
        await using var seed = new AppDbContext(options);

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        npm.AddCustomSection(section);
        var stall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental,
            dailyRate: stallOwnRate, customSectionName: section);

        seed.Facilities.Add(npm);
        seed.Stalls.Add(stall);
        seed.FacilityRates.Add(FacilityRate.Create(
            FacilityCode.NPM, FeeRateKey.NpmDailyStall, marketRate, new DateOnly(2020, 1, 1)));

        if (sectionRate is { } rate)
            seed.FacilitySectionRates.Add(FacilitySectionRate.Create(
                FacilityCode.NPM, section, rate, new DateOnly(2020, 1, 1)));

        await seed.SaveChangesAsync();
        return options;
    }

    [Fact]
    public async Task ItStatesTheSectionsFeeWhereTheOfficePricedTheSectionApart()
    {
        var options = await SeedAsync(marketRate: 30m, sectionRate: 25m, stallOwnRate: null);

        await using var ctx = new AppDbContext(options);
        var stall = Assert.Single(await new StallRepository(ctx).GetStallsByFacilityAsync(FacilityCode.NPM, null, default));

        Assert.Equal(25m, stall.ResolvedDailyFee);
        // And the stored rate stays empty, because nothing was recorded against this stall.
        Assert.Null(stall.DailyRate);
    }

    [Fact]
    public async Task AStallLetAtItsOwnRateStatesThat()
    {
        var options = await SeedAsync(marketRate: 30m, sectionRate: 25m, stallOwnRate: 40m);

        await using var ctx = new AppDbContext(options);
        var stall = Assert.Single(await new StallRepository(ctx).GetStallsByFacilityAsync(FacilityCode.NPM, null, default));

        Assert.Equal(40m, stall.ResolvedDailyFee);
        Assert.Equal(40m, stall.DailyRate);
    }

    [Fact]
    public async Task WithNothingPricedApartItStatesTheMarketsRate()
    {
        // Every office today, and the reading the three screens already showed — so nothing they display moves.
        var options = await SeedAsync(marketRate: 30m, sectionRate: null, stallOwnRate: null);

        await using var ctx = new AppDbContext(options);
        var stall = Assert.Single(await new StallRepository(ctx).GetStallsByFacilityAsync(FacilityCode.NPM, null, default));

        Assert.Equal(30m, stall.ResolvedDailyFee);
    }

    [Fact]
    public async Task TheStoredRateAndTheResolvedFeeAreKeptApart()
    {
        // The distinction the forms depend on: a stall following its section carries no rate of its own, and a form that
        // pre-filled the resolved figure would stamp it as one, detaching the stall from the section for ever.
        var options = await SeedAsync(marketRate: 30m, sectionRate: 25m, stallOwnRate: null);

        await using var ctx = new AppDbContext(options);
        var stall = Assert.Single(await new StallRepository(ctx).GetStallsByFacilityAsync(FacilityCode.NPM, null, default));

        Assert.Null(stall.DailyRate);
        Assert.Equal(25m, stall.ResolvedDailyFee);
        Assert.NotEqual(stall.DailyRate, stall.ResolvedDailyFee);
    }

    [Fact]
    public async Task AFacilityNotBilledByTheDayStatesNoDailyFee()
    {
        var options = Options();
        await using (var seed = new AppDbContext(options))
        {
            var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
            seed.Facilities.Add(tcc);
            seed.Stalls.Add(Stall.Create(tcc.Id, "B-1", 2400m, ApplicableFees.BaseRental));
            await seed.SaveChangesAsync();
        }

        await using var ctx = new AppDbContext(options);
        var stall = Assert.Single(await new StallRepository(ctx).GetStallsByFacilityAsync(FacilityCode.TCC, null, default));

        Assert.Null(stall.ResolvedDailyFee);
    }
}
