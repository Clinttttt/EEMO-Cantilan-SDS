using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence.Seeders;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Phase1;

public sealed class RevenueClassificationSeederTests : RepositoryTestBase
{
    [Fact]
    public async Task SeedIsIdempotentAndOnlyAssignsConfirmedCantilanInstruments()
    {
        await using var context = NewContext();
        var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: "cantilan-sds");
        var carmen = Municipality.Create("CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Upcoming,
            tenantCode: "carmen");
        context.Municipalities.AddRange(cantilan, carmen);
        await context.SaveChangesAsync();

        var seedDate = new DateOnly(2026, 9, 23);
        await RevenueClassificationSeeder.SeedAsync(context, seedDate);
        await RevenueClassificationSeeder.SeedAsync(context, seedDate);

        var classifications = await context.RevenueClassifications
            .Where(x => x.MunicipalityId == cantilan.Id).ToListAsync();
        var policies = await context.RevenueClassificationPolicies
            .Where(x => x.MunicipalityId == cantilan.Id).ToListAsync();

        Assert.Equal(13, classifications.Count);
        Assert.Equal(13, classifications.Select(x => x.SemanticCode).Distinct().Count());
        Assert.Equal(13, policies.Count);
        Assert.All(policies, x => Assert.Equal(seedDate, x.EffectiveDate));
        Assert.Empty(await context.RevenueClassifications.Where(x => x.MunicipalityId == carmen.Id).ToListAsync());
        Assert.Empty(await context.RevenueClassificationPolicies.Where(x => x.MunicipalityId == carmen.Id).ToListAsync());

        var policyByCode = policies.ToDictionary(
            x => classifications.Single(c => c.Id == x.RevenueClassificationId).SemanticCode);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policyByCode[RevenueClassificationCodes.PermanentStallRent].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policyByCode[RevenueClassificationCodes.Ecf].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policyByCode[RevenueClassificationCodes.FishMeatVendorFee].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policyByCode[RevenueClassificationCodes.WeightAndMeasure].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policyByCode[RevenueClassificationCodes.PenaltiesAndFines].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policyByCode[RevenueClassificationCodes.Slaughterhouse].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.CashTicket, policyByCode[RevenueClassificationCodes.MarketFees].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.CashTicket, policyByCode[RevenueClassificationCodes.Tabo].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.CashTicket, policyByCode[RevenueClassificationCodes.TransportationParking].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.CashTicket, policyByCode[RevenueClassificationCodes.VegetableFruitSpaceRental].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.CashTicket, policyByCode[RevenueClassificationCodes.Wcf].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.CashTicket, policyByCode[RevenueClassificationCodes.LandingBerthing].PermittedInstrumentType);
        Assert.Null(policyByCode[RevenueClassificationCodes.Arrears].PermittedInstrumentType);
    }

    [Fact]
    public async Task SeedDoesNothingWhenCantilanIsNotPresent()
    {
        await using var context = NewContext();
        context.Municipalities.Add(Municipality.Create(
            "CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Upcoming, tenantCode: "carmen"));
        await context.SaveChangesAsync();

        await RevenueClassificationSeeder.SeedAsync(context, new DateOnly(2026, 9, 23));

        Assert.Empty(await context.RevenueClassifications.ToListAsync());
        Assert.Empty(await context.RevenueClassificationPolicies.ToListAsync());
    }
}
