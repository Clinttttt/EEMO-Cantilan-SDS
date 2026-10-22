using EEMOCantilanSDS.Application.Common.Onboarding;

namespace EEMOCantilanSDS.Testing.Application.Onboarding;

public sealed class OnboardingTerminalSectionsTests
{
    [Fact]
    public void LegacyDraftWithoutTerminalBlock_RemainsCompatible()
    {
        Assert.Empty(OnboardingTerminalSections.Validate("""{"facilities":[]}"""));
    }

    [Fact]
    public void ExplicitEnabledAndDisabledSections_ValidateWithoutFinancialPolicy()
    {
        var json = """
            {"terminalSections":{"version":1,"items":[
              {"section":"ComfortRoom","isEnabled":false,"localDisplayName":null},
              {"section":"PullPulVansCargoVans","isEnabled":true,"localDisplayName":"Van terminal"},
              {"section":"Tricycad","isEnabled":false,"localDisplayName":null}
            ]}}
            """;
        Assert.Empty(OnboardingTerminalSections.Validate(json));
    }

    [Fact]
    public void MalformedNewerOrAmbiguousSections_AreRefused()
    {
        Assert.NotEmpty(OnboardingTerminalSections.Validate("""{"terminalSections":{"version":2,"items":[]}}"""));
        Assert.NotEmpty(OnboardingTerminalSections.Validate("""{"terminalSections":{"version":1,"items":[]}}"""));
        Assert.NotEmpty(OnboardingTerminalSections.Validate("""
            {"terminalSections":{"version":1,"items":[
              {"section":"ComfortRoom","isEnabled":true,"localDisplayName":"Same"},
              {"section":"PullPulVansCargoVans","isEnabled":true,"localDisplayName":"same"},
              {"section":"Tricycad","isEnabled":true,"localDisplayName":null}
            ]}}
            """));
    }
}
