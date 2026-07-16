using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record FacilitySidebarSummaryDto(
    FacilityCode Code,
    string Name,
    string ShortName,
    int UnpaidCount,
    string? VegetableSectionLabel = null,
    string? FishSectionLabel = null,
    string? MeatSectionLabel = null
);
