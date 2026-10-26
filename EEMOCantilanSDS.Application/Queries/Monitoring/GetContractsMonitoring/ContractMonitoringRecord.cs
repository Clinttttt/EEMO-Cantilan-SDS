using EEMOCantilanSDS.Application.Dtos.Monitoring;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Queries.Monitoring.GetContractsMonitoring;

/// <summary>Source-neutral read projection, not persistence or a financial assessment.</summary>
public sealed class ContractMonitoringRecord
{
    public ContractMonitoringSourceType SourceType { get; init; }
    public Guid SourceRecordId { get; init; }
    public Guid? FacilityId { get; init; }
    public FacilityCode? FacilityCode { get; init; }
    public string Operation { get; init; } = "";
    public string OperationDisplayName { get; init; } = "";
    public Guid? StallId { get; init; }
    public ObligationKind? ObligationKind { get; init; }
    public Guid? PayorId { get; init; }
    public string? ActualOccupant { get; init; }
    public string? ContractName { get; init; }
    public string SpaceNumber { get; init; } = "";
    public OccupancyArrangement? OccupancyBasis { get; init; }
    public string? ContractReference { get; init; }
    public DateOnly OccupancyStart { get; init; }
    public DateOnly? EffectivityDate { get; init; }
    public int? DurationYears { get; init; }
    /// <summary>Extra months beyond DurationYears (a months-only space term has 0 years). Null = yearly.</summary>
    public int? DurationMonths { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public bool Ended { get; init; }
    public DateOnly? ClosureDate { get; init; }
    public decimal? ApprovedCharge { get; init; }
    public ContractMonitoringChargeFrequency ChargeFrequency { get; init; }
    public decimal? ContractStatedAmount { get; init; }
    public decimal? AreaSqm { get; init; }
    public string? AreaLocation { get; init; }
}
