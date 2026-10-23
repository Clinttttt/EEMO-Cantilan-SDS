using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record FacilitySidebarSummaryDto(
    FacilityCode Code,
    string Name,
    string ShortName,
    int UnpaidCount,
    string? VegetableSectionLabel = null,
    string? FishSectionLabel = null,
    string? MeatSectionLabel = null,
    /// <summary>How the facility bills, as the LGU declared it. Null from an older API; a Public Market that reads MonthlyRental is rented by the month.</summary>
    BillingArchetype? Archetype = null,
    CustomFacilityCategory CustomCategory = CustomFacilityCategory.StallRent
)
{
    /// <summary>A Public Market whose LGU declared standard monthly rental: managed like TCC/NCC, not on the daily calendar.</summary>
    public bool IsMonthlyRentMarket => Code == FacilityCode.NPM && Archetype == BillingArchetype.MonthlyRental;
}
