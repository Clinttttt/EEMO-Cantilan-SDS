using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Monitoring;

public enum ContractMonitoringSourceType { FacilityContract = 1, ObligationAccount = 2 }
public enum ContractMonitoringStatus { Active = 1, ExpiringSoon = 2, Expired = 3, NoContract = 4, Ended = 5, Unknown = 6, NotYetEffective = 7 }
public enum ContractMonitoringChargeFrequency { Monthly = 1, Daily = 2, Unknown = 3 }

public sealed record ContractsMonitoringRequest
{
    public string? Search { get; init; }
    public string? Operation { get; init; }
    public Guid? FacilityId { get; init; }
    public string? Status { get; init; }
    public string? OccupancyBasis { get; init; }
    public int? ExpiryWindow { get; init; }
    public DateOnly? AsOf { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string? Sort { get; init; } = "expiry";
}

public sealed record ContractsMonitoringSummaryDto(int ActiveCount, int ExpiringIn7DaysCount, int ExpiringIn30DaysCount,
    int ExpiredCount, int NoContractCount, int EndedCount, int UnknownCount, int NotYetEffectiveCount, int TotalMonitoredOccupancies);

public sealed record ContractsMonitoringPageDto(DateOnly AsOf, ContractsMonitoringSummaryDto Summary,
    int TotalCount, int Page, int PageSize, IReadOnlyList<ContractMonitoringRowDto> Items);

public sealed record ContractMonitoringRowDto(
    ContractMonitoringSourceType SourceType, Guid SourceRecordId, Guid? FacilityId, FacilityCode? FacilityCode,
    string Operation, string OperationDisplayName, Guid? StallId, ObligationKind? ObligationKind, Guid? PayorId,
    string? ActualOccupant, string? ContractName, string SpaceNumber, OccupancyArrangement? OccupancyBasis,
    string? ContractReference, DateOnly OccupancyStart, DateOnly? EffectivityDate, int? DurationYears,
    DateOnly? ExpiryDate, int? DaysUntilExpiry, ContractMonitoringStatus Status, DateOnly? ClosureDate,
    decimal? ApprovedCharge, ContractMonitoringChargeFrequency ChargeFrequency, decimal? ContractStatedAmount,
    decimal? AreaSqm, string? AreaLocation, int? DurationMonths = null);
