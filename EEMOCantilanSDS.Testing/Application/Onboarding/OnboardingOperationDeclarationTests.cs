using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Application.Command.Onboarding.UpdateOnboardingConfig;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Onboarding;

/// <summary>
/// An LGU's declared revenue operations are initial intent held in the onboarding draft. The server reads only that block:
/// it must accept every draft saved before declarations existed, keep unknown fields from breaking a save, and refuse a
/// code it does not recognise rather than store a name that could later be mistaken for a financial identity.
/// </summary>
public class OnboardingOperationDeclarationTests
{
    private static IReadOnlyList<string> Check(string? json) => OnboardingOperationDeclarations.Validate(json);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("""{"facilities":[],"administrator":{"name":"A"},"branding":{"officeName":"X"}}""")]
    [InlineData("""{"operations":null}""")]
    [InlineData("""{"operations":{"version":1}}""")]
    public void AVersionlessOrDeclarationFreeDraftPasses(string? json) => Assert.Empty(Check(json));

    [Fact]
    public void ADeclarationWithALocalNameAndUnknownFieldsPasses()
    {
        var json = """
            {"futureField":{"x":1},
             "operations":{"version":1,"later":true,"items":[
               {"operationCode":"FIESTA_ARAW_LOT_RENTAL","selected":true,"localDisplayName":"Pista Lot Rental","note":"ignored"},
               {"operationCode":"ECF","selected":true},
               {"operationCode":"WCF","selected":false}]}}
            """;
        Assert.Empty(Check(json));
    }

    [Fact]
    public void ALocalNameNeverChangesWhichOperationItIs()
    {
        // Two LGUs naming the same operation differently are the same operation; the name is not read.
        var a = """{"operations":{"items":[{"operationCode":"KANMANGGAY_SPACE_RENTAL","selected":true,"localDisplayName":"Pasalubong Space"}]}}""";
        var b = """{"operations":{"items":[{"operationCode":"KANMANGGAY_SPACE_RENTAL","selected":true}]}}""";
        Assert.Empty(Check(a));
        Assert.Empty(Check(b));
    }

    [Fact]
    public void AnUnrecognisedCodeIsRefused_EvenIfItLooksLikeAKnownOne()
    {
        var errors = Check("""{"operations":{"items":[{"operationCode":"Kanmanggay Space Rental","selected":true}]}}""");
        Assert.Single(errors);
        Assert.Contains("not a revenue operation", errors[0]);
    }

    [Fact]
    public void FacilityOwnedOperationsAreNotDeclarable()
    {
        foreach (var code in new[] { CollectorOperationCodes.Slaughterhouse, CollectorOperationCodes.Tabo, CollectorOperationCodes.NpmDaily, CollectorOperationCodes.Terminal })
            Assert.NotEmpty(Check($$$"""{"operations":{"items":[{"operationCode":"{{{code}}}","selected":true}]}}"""));
    }

    [Fact]
    public void ADuplicateDeclarationIsRefused() =>
        Assert.Contains(Check("""{"operations":{"items":[{"operationCode":"WCF","selected":true},{"operationCode":"WCF","selected":false}]}}"""),
            e => e.Contains("more than once"));

    [Fact]
    public void AnOverlongOrControlCharacterNameIsRefused()
    {
        var long81 = new string('a', 81);
        Assert.NotEmpty(Check($$$"""{"operations":{"items":[{"operationCode":"WCF","localDisplayName":"{{{long81}}}"}]}}"""));
        Assert.NotEmpty(Check("""{"operations":{"items":[{"operationCode":"WCF","localDisplayName":"a\u0007b"}]}}"""));
    }

    [Theory]
    [InlineData("""{"operations":[]}""")]
    [InlineData("""{"operations":{"items":{}}}""")]
    [InlineData("""{"operations":{"version":99,"items":[]}}""")]
    [InlineData("""{"operations":{"items":[{"operationCode":"WCF","selected":"yes"}]}}""")]
    [InlineData("""{"operations":{"items":[{"operationCode":"WCF","localDisplayName":5}]}}""")]
    [InlineData("not json")]
    public void MalformedDeclarationsAreRefusedWithAMessage(string json) => Assert.NotEmpty(Check(json));

    [Fact]
    public void SavingValidatesTheBlock()
    {
        var validator = new UpdateOnboardingConfigCommandValidator();
        Assert.True(validator.Validate(new UpdateOnboardingConfigCommand("t", """{"facilities":[]}""")).IsValid);
        var bad = validator.Validate(new UpdateOnboardingConfigCommand("t", """{"operations":{"items":[{"operationCode":"NOPE"}]}}"""));
        Assert.False(bad.IsValid);
    }

    // ── Custom facility identity at activation ─────────────────────────────────────────────────────────

    private static ActivateMunicipalityCommand Activation(FacilityCode code, BillingArchetype archetype) =>
        new("CARRASCAL",
            new ActivationBranding("Economic Enterprise Office", null, null),
            new ActivationAdministrator("Ana Cruz", "carrascal.head", "acruz@lgu.gov.ph"),
            [new ActivationFacility(code, "Night Market", "NM", archetype)],
            []);

    [Fact]
    public void ACustomCodeAcceptsAMonthlyRentalFacility() =>
        Assert.True(new ActivateMunicipalityCommandValidator().Validate(Activation(FacilityCode.Custom1, BillingArchetype.MonthlyRental)).IsValid);

    [Theory]
    [InlineData(BillingArchetype.DailyStall)]
    [InlineData(BillingArchetype.PerHead)]
    [InlineData(BillingArchetype.Custom)]
    public void ACustomCodeRefusesAnyOtherArchetype(BillingArchetype archetype)
    {
        var result = new ActivateMunicipalityCommandValidator().Validate(Activation(FacilityCode.Custom2, archetype));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("monthly-rental"));
    }
}
