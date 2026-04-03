using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos;

public record CollectorDto(
    Guid Id,
    string FullName,
    string EmployeeId,
    string Username,
    string Email,
    string ContactNumber,
    bool IsActive,
    List<FacilityCode> AssignedFacilities);
