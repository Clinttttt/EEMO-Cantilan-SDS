using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record MobileMenuDto(
    Guid CollectorId,
    string CollectorName,
    string EmployeeId,
    DateOnly Today,
    IReadOnlyList<MobileFacilityMenuItemDto> Facilities);

public sealed record MobileFacilityMenuItemDto(
    FacilityCode Code,
    string Name,
    string Description,
    bool IsAssigned,
    bool IsAvailable);
