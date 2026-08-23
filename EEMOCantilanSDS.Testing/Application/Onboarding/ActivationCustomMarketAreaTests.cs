using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Onboarding;

/// <summary>
/// A market's OWN areas, beyond the three the platform keys on.
///
/// The platform organises a daily sheet on three collection areas (<see cref="MarketSection"/>), and an office names
/// each of them in its own language. A market that also has a rice section, a dry goods row or a carinderia line kept
/// those in a separate per-facility registry (<c>Facility.CustomSectionNames</c>) that onboarding could not reach: the
/// only way one came into being was for the Head to type the name into the first stall filed under it. An office now
/// declares them with the rest of its market, and they are registered when it is activated.
///
/// The rules here are the portal's own (<c>AddNpmCustomSectionCommandValidator</c> and
/// <c>Facility.AddCustomSection</c>), so an area declared at onboarding and one added later cannot behave differently.
/// </summary>
public class ActivationCustomMarketAreaTests
{
    private static ActivateMunicipalityCommand Command(
        IReadOnlyList<string>? areas,
        ActivationSectionLabels? labels = null,
        BillingArchetype archetype = BillingArchetype.DailyStall,
        FacilityCode code = FacilityCode.NPM) =>
        new(
            MunicipalityCode: "CARRASCAL",
            Branding: new ActivationBranding("Economic Enterprise & Management Office", null, null),
            Administrator: new ActivationAdministrator("Ana Cruz", "carrascal.head", "acruz@lgu.gov.ph"),
            Facilities: new List<ActivationFacility>
            {
                new(code, "Carrascal Public Market", "CPM", archetype, null, labels, areas),
            },
            Rates: code == FacilityCode.NPM
                ? new List<ActivationRate> { new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m) }
                : new List<ActivationRate>());

    private static bool Validate(ActivateMunicipalityCommand command, out string? error)
    {
        var result = new ActivateMunicipalityCommandValidator().Validate(command);
        error = string.Join(" | ", result.Errors.Select(e => e.ErrorMessage));
        return result.IsValid;
    }

    [Fact]
    public void AMarketMayDeclareItsOwnAreasAlongsideTheThree()
    {
        var ok = Validate(Command(
            areas: new[] { "Rice Section", "Dry Goods", "Carinderia" },
            labels: new ActivationSectionLabels("Gulayan", "Isda", "Karne")), out var error);

        Assert.True(ok, error);
    }

    [Fact]
    public void AnAreaWithNoNameIsRefused()
    {
        // There is nothing to register, and nothing to print on a sheet.
        var ok = Validate(Command(areas: new[] { "Rice Section", "  " }), out var error);

        Assert.False(ok);
        Assert.Contains("needs a name", error);
    }

    [Fact]
    public void AnAreaNameLongerThanTheRegistryAllowsIsRefused()
    {
        var ok = Validate(Command(areas: new[] { new string('x', 61) }), out var error);

        Assert.False(ok);
        Assert.Contains("60 characters", error);
    }

    [Fact]
    public void TheSameAreaDeclaredTwiceIsRefused()
    {
        // The registry is case-insensitive, so these are one area, and a second row would be silently dropped.
        var ok = Validate(Command(areas: new[] { "Rice Section", "rice section" }), out var error);

        Assert.False(ok);
        Assert.Contains("declared twice", error);
    }

    [Fact]
    public void AnAreaNamedAfterOneOfTheThreeIsRefused()
    {
        // Two groups reading "Gulayan" on one collection sheet, one canonical and one custom, is not a document
        // anybody can reconcile.
        var ok = Validate(Command(
            areas: new[] { "Gulayan" },
            labels: new ActivationSectionLabels("Gulayan", "Isda", "Karne")), out var error);

        Assert.False(ok);
        Assert.Contains("same name as one of the three", error);
    }

    [Fact]
    public void OnlyADailyStallMarketHasAreasToDeclare()
    {
        // A monthly-rental facility keeps no section registry, so accepting areas for it would store nothing and say
        // nothing - which is how an office ends up believing it declared something it did not.
        var ok = Validate(Command(
            areas: new[] { "Rice Section" },
            archetype: BillingArchetype.MonthlyRental,
            code: FacilityCode.TCC), out var error);

        Assert.False(ok);
        Assert.Contains("no market areas to declare", error);
    }

    [Fact]
    public void AMarketThatDeclaresNoneIsUnaffected()
    {
        Assert.True(Validate(Command(areas: null), out var e1), e1);
        Assert.True(Validate(Command(areas: Array.Empty<string>()), out var e2), e2);
    }
}
