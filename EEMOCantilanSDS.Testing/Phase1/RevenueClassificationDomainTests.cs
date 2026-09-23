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
}
