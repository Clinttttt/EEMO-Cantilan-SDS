using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Onboarding;

/// <summary>
/// The rule an office declares for its market month, at onboarding.
///
/// <para>
/// Declared with the rest of the market so an office's FIRST month is measured by its own convention rather than by
/// somebody else's. Both bases are ordinary ordinances: a month let for a rent and collected in installments, or a month
/// that owes the days it has. It stays changeable in Facility Configuration afterwards, because an office that finds its
/// paper says otherwise must not need a developer.
/// </para>
///
/// <para>
/// The rule these hold above all: an activation that says NOTHING about a basis behaves exactly as this platform always
/// has. Every activation recorded before the basis existed said nothing.
/// </para>
/// </summary>
public class ActivationMonthBasisTests
{
    private static ActivateMunicipalityCommand Command(
        NpmMonthBasis basis = NpmMonthBasis.RentGoal,
        decimal monthlyRent = 0m,
        FacilityCode code = FacilityCode.NPM)
    {
        var rates = new List<ActivationRate> { new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m) };
        if (monthlyRent > 0m) rates.Add(new(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, monthlyRent));

        return new(
            MunicipalityCode: "CARRASCAL",
            Branding: new ActivationBranding("Economic Enterprise & Management Office", null, null),
            Administrator: new ActivationAdministrator("Ana Cruz", "carrascal.head", "acruz@lgu.gov.ph"),
            Facilities: new List<ActivationFacility>
            {
                new(code, "Carrascal Public Market", "CPM", BillingArchetype.DailyStall,
                    StallGroups: null, SectionLabels: null, CustomSections: null, MonthBasis: basis),
            },
            Rates: rates);
    }

    private static bool Validate(ActivateMunicipalityCommand command, out string error)
    {
        var result = new ActivateMunicipalityCommandValidator().Validate(command);
        error = string.Join(" | ", result.Errors.Select(e => e.ErrorMessage));
        return result.IsValid;
    }

    [Fact]
    public void AnActivationThatSaysNothingIsOnTheMonthlyGoal()
    {
        // Every activation recorded before the basis existed said nothing, and each of them measured a month by its rent.
        var market = Command().Facilities[0];

        Assert.Equal(NpmMonthBasis.RentGoal, market.MonthBasis);
    }

    [Fact]
    public void AnOfficeMayDeclareTheDaysBasisAtOnboarding()
    {
        var command = Command(NpmMonthBasis.PureDays);

        Assert.True(Validate(command, out var error), error);
        Assert.Equal(NpmMonthBasis.PureDays, command.Facilities[0].MonthBasis);
    }

    [Fact]
    public void TheDaysBasisWithAMonthlyRentIsRefused()
    {
        // A contradiction, and one the rule would resolve silently by ignoring the figure. Refused at the door instead, so
        // the office's own rate table never holds a monthly amount its screens say it does not use.
        var command = Command(NpmMonthBasis.PureDays, monthlyRent: 900m);

        Assert.False(Validate(command, out var error));
        Assert.Contains("cannot also state a monthly rent", error);
    }

    [Fact]
    public void TheMonthlyGoalWithAMonthlyRentIsExactlyRight()
    {
        var command = Command(NpmMonthBasis.RentGoal, monthlyRent: 900m);

        Assert.True(Validate(command, out var error), error);
    }

    [Fact]
    public void AMonthlyRentOfNoughtIsNotAContradiction()
    {
        // Nought is a withdrawn figure everywhere else in this platform, so it is not a stated monthly rent here either.
        var command = Command(NpmMonthBasis.PureDays, monthlyRent: 0m);

        Assert.True(Validate(command, out var error), error);
    }
}
