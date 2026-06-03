using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Facilities;

public record FacilitySidebarSummaryDto(
    FacilityCode Code,
    string Name,
    string ShortName,
    int UnpaidCount
);
