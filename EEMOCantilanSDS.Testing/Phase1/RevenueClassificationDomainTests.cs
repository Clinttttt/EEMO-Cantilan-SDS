using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Phase1;

public sealed class RevenueClassificationDomainTests
{
    [Fact]
    public void SemanticIdentityIsStableWhileDisplayPolicyCanBeVersioned()
    {
        var classification = RevenueClassification.Create("MARKET_FEES");
        var first = RevenueClassificationPolicy.Create(
            classification.Id, new DateOnly(2026, 9, 1), "Market Fees", RevenueInstrumentType.CashTicket);
        var renamed = RevenueClassificationPolicy.Create(
            classification.Id, new DateOnly(2027, 1, 1), "General Market Fees", RevenueInstrumentType.CashTicket);

        Assert.Equal("MARKET_FEES", classification.SemanticCode);
        Assert.Equal("Market Fees", classification.ResolvePolicyAsOf([first, renamed], new DateOnly(2026, 12, 31))!.DisplayName);
        Assert.Equal("General Market Fees", classification.ResolvePolicyAsOf([first, renamed], new DateOnly(2027, 1, 1))!.DisplayName);
        Assert.Equal("MARKET_FEES", classification.SemanticCode);
    }

    [Fact]
    public void RetirementDoesNotEraseIdentityOrPolicyHistory()
    {
        var classification = RevenueClassification.Create("ECF");
        var policy = RevenueClassificationPolicy.Create(
            classification.Id, new DateOnly(2026, 9, 1), "ECF", RevenueInstrumentType.OfficialReceipt);

        classification.Retire("head");

        Assert.False(classification.IsActive);
        Assert.Equal("ECF", classification.SemanticCode);
        Assert.Equal("ECF", policy.DisplayName);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, policy.PermittedInstrumentType);
    }

    [Theory]
    [InlineData("")]
    [InlineData("market-fees")]
    [InlineData("1MARKET")]
    [InlineData("A")]
    public void InvalidSemanticCodesAreRejected(string semanticCode) =>
        Assert.Throws<ArgumentException>(() => RevenueClassification.Create(semanticCode));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PolicyRequiresDisplayName(string displayName) =>
        Assert.Throws<ArgumentException>(() => RevenueClassificationPolicy.Create(
            Guid.NewGuid(), new DateOnly(2026, 9, 1), displayName, RevenueInstrumentType.OfficialReceipt));

    [Fact]
    public void AsOfResolutionReturnsNoPolicyBeforeFirstEffectiveDate()
    {
        var classification = RevenueClassification.Create("TABO");
        var policy = RevenueClassificationPolicy.Create(
            classification.Id, new DateOnly(2026, 9, 1), "Tabo", RevenueInstrumentType.CashTicket);

        Assert.Null(classification.ResolvePolicyAsOf([policy], new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void AsOfResolutionIgnoresAnotherClassificationAndMunicipality()
    {
        var municipalityA = Guid.NewGuid();
        var municipalityB = Guid.NewGuid();
        var classificationA = RevenueClassification.Create("MARKET_FEES", municipalityA);
        var classificationB = RevenueClassification.Create("MARKET_FEES", municipalityB);
        var policyA = RevenueClassificationPolicy.Create(classificationA.Id, new DateOnly(2026, 9, 1),
            "A policy", RevenueInstrumentType.CashTicket, municipalityA);
        var policyB = RevenueClassificationPolicy.Create(classificationB.Id, new DateOnly(2027, 1, 1),
            "B policy", RevenueInstrumentType.OfficialReceipt, municipalityB);

        Assert.Equal("A policy", classificationA.ResolvePolicyAsOf([policyA, policyB], new DateOnly(2027, 2, 1))!.DisplayName);
    }

    [Fact]
    public void PolicyContextsAreExplicitAndResolveIndependently()
    {
        var tenant = Guid.NewGuid();
        var classification = RevenueClassification.Create("VEGETABLE_FRUIT_SPACE_RENTAL", tenant);
        var effectiveDate = new DateOnly(2026, 9, 27);
        var defaultPolicy = RevenueClassificationPolicy.Create(
            classification.Id, new DateOnly(2026, 9, 1), "Legacy default", null, tenant);
        var wholePayment = RevenueClassificationPolicy.Create(
            classification.Id, effectiveDate, "Whole payment", RevenueInstrumentType.OfficialReceipt,
            tenant, businessContext: RevenuePolicyContext.VegetableWholePayment);
        var dailyTransaction = RevenueClassificationPolicy.Create(
            classification.Id, effectiveDate, "Daily transaction", RevenueInstrumentType.CashTicket,
            tenant, businessContext: RevenuePolicyContext.VegetableDailyTransaction);
        var policies = new[] { defaultPolicy, wholePayment, dailyTransaction };

        Assert.Equal(RevenuePolicyContext.Default, defaultPolicy.BusinessContext);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt,
            classification.ResolvePolicyAsOf(policies, effectiveDate, RevenuePolicyContext.VegetableWholePayment)!
                .PermittedInstrumentType);
        Assert.Equal(RevenueInstrumentType.CashTicket,
            classification.ResolvePolicyAsOf(policies, effectiveDate, RevenuePolicyContext.VegetableDailyTransaction)!
                .PermittedInstrumentType);
        Assert.Null(classification.ResolvePolicyAsOf([dailyTransaction], effectiveDate,
            RevenuePolicyContext.VegetableWholePayment));
        Assert.Null(classification.ResolvePolicyAsOf([wholePayment], effectiveDate,
            RevenuePolicyContext.VegetableDailyTransaction));
        Assert.Null(classification.ResolvePolicyAsOf(policies, effectiveDate)!.PermittedInstrumentType);
        Assert.Null(classification.ResolvePolicyAsOf([], effectiveDate, RevenuePolicyContext.VegetableWholePayment));
        Assert.Throws<ArgumentOutOfRangeException>(() => classification.ResolvePolicyAsOf(
            policies, effectiveDate, (RevenuePolicyContext)999));
    }

    [Fact]
    public void OnlyKnownContextsAreAccepted_AndContextualPolicyRequiresAnInstrument()
    {
        var classification = RevenueClassification.Create("VEGETABLE_FRUIT_SPACE_RENTAL");

        Assert.Throws<ArgumentOutOfRangeException>(() => RevenueClassificationPolicy.Create(
            classification.Id, new DateOnly(2026, 9, 27), "Unknown", RevenueInstrumentType.CashTicket,
            businessContext: (RevenuePolicyContext)999));
        Assert.Throws<ArgumentException>(() => RevenueClassificationPolicy.Create(
            classification.Id, new DateOnly(2026, 9, 27), "Whole payment", null,
            businessContext: RevenuePolicyContext.VegetableWholePayment));

        var unresolvedDefault = RevenueClassificationPolicy.Create(
            classification.Id, new DateOnly(2026, 9, 27), "Unresolved", null);
        Assert.Null(unresolvedDefault.PermittedInstrumentType);
    }
}
