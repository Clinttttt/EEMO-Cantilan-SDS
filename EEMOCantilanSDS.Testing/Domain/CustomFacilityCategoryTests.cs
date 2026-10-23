using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

public sealed class CustomFacilityCategoryTests
{
    [Fact]
    public void LegacyCustomFacilitiesDefaultToStallRentWithoutChangingMonthlyBilling()
    {
        var legacy = Facility.Create(FacilityCode.Custom1, "Old market hall", "OMH");
        Assert.Equal(CustomFacilityCategory.StallRent, legacy.CustomCategory);
        Assert.Equal(BillingArchetype.MonthlyRental, legacy.Archetype);
    }

    [Fact]
    public void NewCustomSpaceRetainsMonthlyRentalEngineAndStableFacilityIdentity()
    {
        var tenant = Guid.NewGuid();
        var facility = Facility.Create(FacilityCode.Custom2, "Civic Plaza", "PLZ",
            municipalityId: tenant, customCategory: CustomFacilityCategory.SpaceRental);
        Assert.Equal(tenant, facility.MunicipalityId);
        Assert.Equal(FacilityCode.Custom2, facility.Code);
        Assert.Equal(BillingArchetype.MonthlyRental, facility.Archetype);
        Assert.Equal(CustomFacilityCategory.SpaceRental, facility.CustomCategory);
    }

    [Fact]
    public void BuiltInFacilityCannotMasqueradeAsCustomSpace()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Facility.Create(FacilityCode.TCC, "TCC", "TCC",
                customCategory: CustomFacilityCategory.SpaceRental));
    }
}
