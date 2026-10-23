using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Onboarding;

public sealed class ActivateMunicipalityWeighingValidatorTests
{
    private static ActivateMunicipalityCommand Build(ActivationWeighingConfiguration config,
        bool selected = true) => new("TESTLGU",
            new ActivationBranding("LGU enterprise office", null, null),
            new ActivationAdministrator("Ana Cruz", "lguh", "head@lgu.gov.ph"),
            [new ActivationFacility(FacilityCode.NPM, "Public Market", "Market", BillingArchetype.DailyStall)],
            [], OperationDeclarations: [new ActivationOperation("WEIGHT_AND_MEASURE", selected)],
            WeighingConfiguration: config);

    [Theory]
    [InlineData(true, 2.5, false, null)]
    [InlineData(false, null, true, 60.0)]
    [InlineData(true, 1.0, true, 66.0)]
    [InlineData(false, null, false, null)]
    public void Valid_independent_fish_meat_combinations(bool fish, double? fishRate, bool meat, double? meatRate)
    {
        var command = Build(new(fish, fishRate is { } f ? (decimal)f : null,
            meat, meatRate is { } m ? (decimal)m : null));
        Assert.True(new ActivateMunicipalityCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void Rejects_invalid_rate_and_undeclared_enabled_service()
    {
        var invalid = Build(new(true, -1m, false, null));
        Assert.Contains(new ActivateMunicipalityCommandValidator().Validate(invalid).Errors,
            x => x.ErrorMessage.Contains("per-kg"));
        var undeclared = Build(new(false, null, true, 60m), false);
        Assert.Contains(new ActivateMunicipalityCommandValidator().Validate(undeclared).Errors,
            x => x.ErrorMessage.Contains("must be declared"));
    }
}
