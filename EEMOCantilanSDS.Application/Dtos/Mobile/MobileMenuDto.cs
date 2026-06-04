using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record MobileMenuDto(
    Guid CollectorId,
    string CollectorName,
    string EmployeeId,
    DateOnly Today,
    IReadOnlyList<MobileFacilityMenuItemDto> AssignedFacilities);

public sealed record MobileFacilityMenuItemDto(
    FacilityCode Code,
    string Name,
    string Description,
    bool IsAvailable);
