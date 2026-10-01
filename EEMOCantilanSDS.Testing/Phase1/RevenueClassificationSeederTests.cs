using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence.Seeders;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Phase1;

public sealed class RevenueClassificationSeederTests : RepositoryTestBase
{
    private static readonly DateOnly ClarificationDate = new(2026, 9, 27);

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
        await RevenueClassificationSeeder.SeedAsync(context, new DateOnly(2026, 9, 24));

        var classifications = await context.RevenueClassifications
            .Where(x => x.MunicipalityId == cantilan.Id).ToListAsync();
        var policies = await context.RevenueClassificationPolicies
            .Where(x => x.MunicipalityId == cantilan.Id).ToListAsync();

        Assert.Equal(17, classifications.Count);
        Assert.Equal(17, classifications.Select(x => x.SemanticCode).Distinct().Count());
        Assert.Equal(17, policies.Count);
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
        // IA-049: Transfer Large Cattle is an Official Receipt operation.
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policyByCode[RevenueClassificationCodes.TransferLargeCattle].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policyByCode[RevenueClassificationCodes.IcePlant].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policyByCode[RevenueClassificationCodes.KanmanggaySpaceRental].PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policyByCode[RevenueClassificationCodes.FiestaArawLotRental].PermittedInstrumentType);
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

    [Fact]
    public async Task FreshPreClarificationSeedKeepsHistoricalTaboAndVegetableDefaults()
    {
        await using var context = NewContext();
        var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: "cantilan-sds");
        context.Municipalities.Add(cantilan);
        await context.SaveChangesAsync();

        var seedDate = ClarificationDate.AddDays(-1);
        await RevenueClassificationSeeder.SeedAsync(context, seedDate);

        var classifications = await context.RevenueClassifications
            .Where(x => x.MunicipalityId == cantilan.Id).ToDictionaryAsync(x => x.SemanticCode);
        var policies = await context.RevenueClassificationPolicies
            .Where(x => x.MunicipalityId == cantilan.Id).ToListAsync();
        var tabo = Assert.Single(policies, x => x.RevenueClassificationId == classifications[RevenueClassificationCodes.Tabo].Id);
        var vegetable = Assert.Single(policies, x => x.RevenueClassificationId == classifications[RevenueClassificationCodes.VegetableFruitSpaceRental].Id);

        Assert.Equal(seedDate, tabo.EffectiveDate);
        Assert.Equal(RevenueInstrumentType.CashTicket, tabo.PermittedInstrumentType);
        Assert.Equal(RevenuePolicyContext.Default, vegetable.BusinessContext);
        Assert.Equal(RevenueInstrumentType.CashTicket, vegetable.PermittedInstrumentType);
        Assert.DoesNotContain(policies, x => x.BusinessContext != RevenuePolicyContext.Default);
    }

    [Fact]
    public async Task FreshPostClarificationSeedUsesTaboOrAndDistinctVegetableContextsIdempotently()
    {
        await using var context = NewContext();
        var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: "cantilan-sds");
        context.Municipalities.Add(cantilan);
        await context.SaveChangesAsync();

        await RevenueClassificationSeeder.SeedAsync(context, ClarificationDate.AddDays(3));
        await RevenueClassificationSeeder.SeedAsync(context, ClarificationDate.AddDays(3));

        var classifications = await context.RevenueClassifications
            .Where(x => x.MunicipalityId == cantilan.Id).ToDictionaryAsync(x => x.SemanticCode);
        var policies = await context.RevenueClassificationPolicies
            .Where(x => x.MunicipalityId == cantilan.Id).ToListAsync();
        var tabo = Assert.Single(policies, x => x.RevenueClassificationId == classifications[RevenueClassificationCodes.Tabo].Id);
        Assert.Equal(ClarificationDate, tabo.EffectiveDate);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, tabo.PermittedInstrumentType);

        var vegetable = policies.Where(x => x.RevenueClassificationId == classifications[RevenueClassificationCodes.VegetableFruitSpaceRental].Id)
            .ToList();
        Assert.Equal(2, vegetable.Count);
        Assert.DoesNotContain(vegetable, x => x.BusinessContext == RevenuePolicyContext.Default);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt,
            Assert.Single(vegetable, x => x.BusinessContext == RevenuePolicyContext.VegetableWholePayment).PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.CashTicket,
            Assert.Single(vegetable, x => x.BusinessContext == RevenuePolicyContext.VegetableDailyTransaction).PermittedInstrumentType);
        Assert.All(vegetable, x => Assert.Equal(ClarificationDate, x.EffectiveDate));
        Assert.Equal(18, policies.Count);
    }

    [Fact]
    public async Task ExistingPreClarificationTaboAndVegetablePoliciesArePreservedAndCorrectedByAppending()
    {
        await using var context = NewContext();
        var cantilan = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: "cantilan-sds");
        var taboClassification = RevenueClassification.Create(RevenueClassificationCodes.Tabo, cantilan.Id);
        var vegetableClassification = RevenueClassification.Create(RevenueClassificationCodes.VegetableFruitSpaceRental, cantilan.Id);
        var earlierDate = ClarificationDate.AddDays(-1);
        var earlierTabo = RevenueClassificationPolicy.Create(taboClassification.Id, earlierDate,
            "Tabo", RevenueInstrumentType.CashTicket, cantilan.Id, description: "Earlier CT policy", createdBy: "seed-v1");
        var earlierVegetable = RevenueClassificationPolicy.Create(vegetableClassification.Id, earlierDate,
            "Vegetable/Fruit Space Rental", RevenueInstrumentType.CashTicket, cantilan.Id,
            description: "Earlier default CT policy", createdBy: "seed-v1");
        context.AddRange(cantilan, taboClassification, vegetableClassification, earlierTabo, earlierVegetable);
        await context.SaveChangesAsync();

        await RevenueClassificationSeeder.SeedAsync(context, ClarificationDate.AddDays(3));
        await RevenueClassificationSeeder.SeedAsync(context, ClarificationDate.AddDays(3));

        var taboRows = await context.RevenueClassificationPolicies
            .Where(x => x.RevenueClassificationId == taboClassification.Id)
            .OrderBy(x => x.EffectiveDate).ToListAsync();
        Assert.Equal(2, taboRows.Count);
        Assert.Equal(earlierTabo.Id, taboRows[0].Id);
        Assert.Equal(earlierDate, taboRows[0].EffectiveDate);
        Assert.Equal(RevenueInstrumentType.CashTicket, taboRows[0].PermittedInstrumentType);
        Assert.Equal("Earlier CT policy", taboRows[0].Description);
        Assert.Equal(ClarificationDate, taboRows[1].EffectiveDate);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, taboRows[1].PermittedInstrumentType);

        var vegetableRows = await context.RevenueClassificationPolicies
            .Where(x => x.RevenueClassificationId == vegetableClassification.Id).ToListAsync();
        Assert.Equal(3, vegetableRows.Count);
        var preservedVegetable = Assert.Single(vegetableRows, x => x.Id == earlierVegetable.Id);
        Assert.Equal(earlierDate, preservedVegetable.EffectiveDate);
        Assert.Equal(RevenuePolicyContext.Default, preservedVegetable.BusinessContext);
        Assert.Equal(RevenueInstrumentType.CashTicket, preservedVegetable.PermittedInstrumentType);
        Assert.Equal("Earlier default CT policy", preservedVegetable.Description);
        Assert.Single(vegetableRows, x => x.BusinessContext == RevenuePolicyContext.VegetableWholePayment);
        Assert.Single(vegetableRows, x => x.BusinessContext == RevenuePolicyContext.VegetableDailyTransaction);
    }
}
