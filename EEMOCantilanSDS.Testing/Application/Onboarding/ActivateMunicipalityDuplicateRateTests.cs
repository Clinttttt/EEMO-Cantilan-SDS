using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Onboarding;

/// <summary>
/// The defect this suite exists for, reported 2026-08-23: activating Carrascal answered the operator with the single
/// word "Conflict" on an LGU that had never been activated. The production log named the cause —
/// <c>23505 duplicate key value violates unique constraint IX_FacilityRates_MunicipalityId_FacilityCode_RateKey_Effective</c>
/// — so the payload carried two rows for one facility and rate key, and Postgres, not the platform, answered the
/// operator.
///
/// The platform holds ONE large-animal rate: carabao and cow both read <see cref="FeeRateKey.SlhLargePerHead"/>. A
/// slaughterhouse that lists both therefore produces two rows under that key. Where both state the same amount they
/// are one statement and one row is filed. Where they state DIFFERENT amounts they contradict each other, and the
/// platform refuses rather than choosing which of an office's two amounts to charge.
/// </summary>
public class ActivateMunicipalityDuplicateRateTests
{
    private static ActivateMunicipalityCommand Command(
        IReadOnlyList<ActivationRate> rates,
        IReadOnlyList<ActivationFacility>? facilities = null,
        IReadOnlyList<ActivationCustomAnimal>? animals = null) =>
        new(
            MunicipalityCode: "CARRASCAL",
            Branding: new ActivationBranding("Economic Enterprise & Management Office", null, null),
            Administrator: new ActivationAdministrator("Ana Cruz", "carrascal.head", "acruz@lgu.gov.ph"),
            Facilities: facilities ?? new List<ActivationFacility>
            {
                new(FacilityCode.NPM, "Carrascal Public Market", "CPM", BillingArchetype.DailyStall),
                new(FacilityCode.SLH, "Carrascal Slaughterhouse", "CSLH", BillingArchetype.PerHead),
            },
            Rates: rates,
            CustomAnimals: animals);

    private static bool Validate(ActivateMunicipalityCommand command, out string? error)
    {
        var result = new ActivateMunicipalityCommandValidator().Validate(command);
        error = string.Join(" | ", result.Errors.Select(e => e.ErrorMessage));
        return result.IsValid;
    }

    [Fact]
    public void TheSameRateStatedTwiceAtTheSameAmount_IsOneStatement_AndIsAccepted()
    {
        // Carabao ₱365 and cow ₱365: one large-animal rate, said twice.
        var ok = Validate(Command(new List<ActivationRate>
        {
            new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m),
            new(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 250m),
            new(FacilityCode.SLH, FeeRateKey.SlhLargePerHead, 365m),
            new(FacilityCode.SLH, FeeRateKey.SlhLargePerHead, 365m),
        }), out var error);

        Assert.True(ok, error);
    }

    [Fact]
    public void TwoDifferentAmountsForOneRate_AreRefused_AndBothAmountsAreNamed()
    {
        // Carabao ₱365 and cow ₱400. The platform cannot hold both, and picking one would be the platform deciding
        // an ordinance, so it says what it was given and leaves the choice with the office.
        var ok = Validate(Command(new List<ActivationRate>
        {
            new(FacilityCode.SLH, FeeRateKey.SlhLargePerHead, 365m),
            new(FacilityCode.SLH, FeeRateKey.SlhLargePerHead, 400m),
        }), out var error);

        Assert.False(ok);
        Assert.Contains("SlhLargePerHead", error);
        Assert.Contains("365", error);
        Assert.Contains("400", error);
        Assert.DoesNotContain("Conflict", error);   // the word the operator used to be given, alone
    }

    [Fact]
    public void TwoFacilitiesUnderOneCode_AreRefused_AndNamed()
    {
        // The other unique index the same payload could have hit. It answered "Conflict" too.
        var ok = Validate(Command(
            rates: new List<ActivationRate> { new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m) },
            facilities: new List<ActivationFacility>
            {
                new(FacilityCode.NPM, "Carrascal Public Market", "CPM", BillingArchetype.DailyStall),
                new(FacilityCode.NPM, "Carrascal Satellite Market", "CSM", BillingArchetype.DailyStall),
            }), out var error);

        Assert.False(ok);
        Assert.Contains("NPM", error);
        Assert.Contains("Carrascal Satellite Market", error);
    }

    [Fact]
    public void OneAnimalWithTwoRates_IsRefused_AndNamed()
    {
        // The registry is keyed by name, so this is the same fault one table over.
        var ok = Validate(Command(
            rates: new List<ActivationRate> { new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m) },
            animals: new List<ActivationCustomAnimal>
            {
                new("Goat", 120m),
                new("goat", 150m),
            }), out var error);

        Assert.False(ok);
        Assert.Contains("120", error);
        Assert.Contains("150", error);
    }

    [Fact]
    public void TheSameAnimalStatedTwiceAtTheSameRate_IsAccepted()
    {
        var ok = Validate(Command(
            rates: new List<ActivationRate> { new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m) },
            animals: new List<ActivationCustomAnimal>
            {
                new("Goat", 120m),
                new("Goat", 120m),
            }), out var error);

        Assert.True(ok, error);
    }
}
