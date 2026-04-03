using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos;

public record CollectorListDto(
    Guid Id,
    string FullName,
    string Email,
    string EmployeeId,
    List<FacilityCode> AssignedFacilities,
    decimal CollectedThisMonth,
    int Transactions,
    DateTime? LastActiveAt,
    bool IsActive);
