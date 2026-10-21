using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>The Public Market rental model an LGU declares at onboarding is what activation stores, and nothing else is inferred or converted.</summary>
public sealed partial class OnboardingActivationTests
{
    [SkippableFact]
    public async Task AMonthlyOnlyPublicMarket_IsStoredAsDeclared_WithItsSections_AndNoDailyRateOrMonthBasis()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();

        var result = await ActivateAsync(w, Command(w,
            [new ActivationFacility(FacilityCode.NPM, "Madrid Public Market", "MPM", BillingArchetype.MonthlyRental, CustomSections: ["Rice Section"])],
            [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 6m)]));

        Assert.True(result.IsSuccess, result.Error);
        await using var verify = db.CreateContext(Guid.Empty);
        var market = await verify.Facilities.IgnoreQueryFilters().SingleAsync(f => f.MunicipalityId == w.TargetId);
        Assert.Equal((FacilityCode.NPM, BillingArchetype.MonthlyRental, true), (market.Code, market.Archetype, market.IsMonthlyRentMarket));
        Assert.Null(market.MonthBasisStatedAt);                                       // a market rented by the month has no daily basis to state
        Assert.Equal(["Rice Section"], market.CustomSectionNames);                    // section registration stays available
        var rates = await verify.FacilityRates.IgnoreQueryFilters().Where(r => r.MunicipalityId == w.TargetId).ToListAsync();
        Assert.Equal([FeeRateKey.NpmFishPerKilo], rates.Select(r => r.RateKey).ToArray());   // no daily or market-wide monthly rate was filed
        Assert.Equal(0, await verify.Stalls.IgnoreQueryFilters().CountAsync());       // stalls, contracts and rents are set in the portal afterwards

        // The reference LGU's daily market is untouched by another LGU's declaration.
        var reference = await verify.Facilities.IgnoreQueryFilters().SingleAsync(f => f.MunicipalityId == w.ReferenceTenantId);
        Assert.Equal((BillingArchetype.DailyStall, false), (reference.Archetype, reference.IsMonthlyRentMarket));
    }

    [SkippableFact]
    public async Task ADailyPublicMarket_StillRecordsItsDeclaredMonthBasis_AsBefore()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();

        var result = await ActivateAsync(w, Command(w,
            [new ActivationFacility(FacilityCode.NPM, "Madrid Pamilihan", "MPM", BillingArchetype.DailyStall, MonthBasis: NpmMonthBasis.PureDays)],
            [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m)]));

        Assert.True(result.IsSuccess, result.Error);
        await using var verify = db.CreateContext(Guid.Empty);
        var market = await verify.Facilities.IgnoreQueryFilters().SingleAsync(f => f.MunicipalityId == w.TargetId);
        Assert.Equal((BillingArchetype.DailyStall, NpmMonthBasis.PureDays, false), (market.Archetype, market.MonthBasis, market.IsMonthlyRentMarket));
        Assert.NotNull(market.MonthBasisStatedAt);
    }
}
