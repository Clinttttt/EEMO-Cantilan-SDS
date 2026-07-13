using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos;

public record CollectorActivityDto(
    Guid Id,
    string FullName,
    string EmployeeId,
    string Email,
    string ContactNumber,
    List<FacilityCode> AssignedFacilities,
    decimal CollectedThisMonth,
    int Transactions,
    int FacilitiesCount,
    DateTime? LastActiveAt,
    List<RecentTransactionDto> RecentTransactions,
    string Username = "");

public record RecentTransactionDto(
    string ORNumber,
    string PayorName,
    FacilityCode Facility,
    string Nature,
    decimal Amount,
    string Status,
    DateTime TransactionDate);
