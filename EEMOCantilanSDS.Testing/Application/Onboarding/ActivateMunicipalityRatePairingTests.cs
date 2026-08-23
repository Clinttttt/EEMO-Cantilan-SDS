using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Onboarding;

/// <summary>
/// Onboarding writes an LGU's ordinance rates. A rate key belongs to one facility, and the resolver only reads
/// a row filed under that facility — so a mis-paired row would be stored, ignored, and leave the LGU looking
/// configured while every billing path charged the platform's default instead. Activation refuses it.
/// </summary>
public class ActivateMunicipalityRatePairingTests
{
    private static ActivateMunicipalityCommand Command(params ActivationRate[] rates) =>
        new(
            MunicipalityCode: "SDS-TEST",
            Branding: new ActivationBranding("Economic Enterprise Office", null, null),
            Administrator: new ActivationAdministrator("Ana Cruz", "acruz", "acruz@lgu.gov.ph"),
            // One facility, because an activation without any is refused for its own reasons — this suite is
            // about the rate pairing, not the rest of the form.
            Facilities: new List<ActivationFacility>
            {
                new(FacilityCode.NPM, "New Public Market", "NPM", BillingArchetype.DailyStall),
            },
            Rates: rates.ToList());

    private static bool Validate(ActivateMunicipalityCommand command, out string? error)
    {
        var result = new ActivateMunicipalityCommandValidator().Validate(command);
        error = result.Errors.Select(e => e.ErrorMessage).FirstOrDefault();
        return result.IsValid;
    }

    [Fact]
    public void ARateFiledUnderItsOwnFacility_IsAccepted()
    {
        var ok = Validate(Command(
            new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m),
            new(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 1_100m),
            new(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 250m),
            new(FacilityCode.TRM, FeeRateKey.TrmPerTrip, 15m)), out var error);

        Assert.True(ok, error);
    }

    [Fact]
    public void ARateFiledUnderTheWrongFacility_IsRefused_AndSaysWhy()
    {
        var ok = Validate(Command(new ActivationRate(FacilityCode.TCC, FeeRateKey.NpmDailyStall, 40m)), out var error);

        Assert.False(ok);
        Assert.Contains("is not a rate of TCC", error);
    }

    [Fact]
    public void AMarketsPerAreaRatesAreItsOwnFacilitysRates()
    {
        // Phase 4 (2026-08-23): onboarding now sends a rate for each area the office priced. They belong to the market's
        // ordinance like its market-wide rate, so activation accepts them for NPM.
        var ok = Validate(Command(
            new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m),
            new(FacilityCode.NPM, FeeRateKey.NpmDailyStallVegetable, 35m),
            new(FacilityCode.NPM, FeeRateKey.NpmDailyStallFish, 30m),
            new(FacilityCode.NPM, FeeRateKey.NpmDailyStallMeat, 40m)), out var error);

        Assert.True(ok, error);
    }

    [Fact]
    public void AnAreasRateFiledUnderAnotherFacility_IsRefused()
    {
        // The same rule as every other key: a row filed against the wrong facility is not that facility's rate, and the
        // resolver would never read it.
        var ok = Validate(Command(new ActivationRate(FacilityCode.TCC, FeeRateKey.NpmDailyStallFish, 30m)), out var error);

        Assert.False(ok);
        Assert.Contains("is not a rate of TCC", error);
    }

    [Fact]
    public void AMonthlyRentalFacility_HasNoOrdinanceRatesToState()
    {
        // TCC/NCC/BBQ/ICE rent is negotiated per stall, so no fixed key belongs to them at all.
        var ok = Validate(Command(new ActivationRate(FacilityCode.NCC, FeeRateKey.NpmMonthlyStall, 900m)), out _);

        Assert.False(ok);
    }
}
