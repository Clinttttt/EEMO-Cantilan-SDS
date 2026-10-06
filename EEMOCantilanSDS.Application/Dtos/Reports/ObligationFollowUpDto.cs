using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Reports;

public enum FollowUpScopeKind { Facility = 1, MonthlySpace = 2, EventLot = 3 }
public sealed record FollowUpScopeDto(FollowUpScopeKind Kind, string OperationCode, string DisplayName, FacilityCode? Facility = null);
public sealed record ObligationFollowUpItemDto(Guid AccountId, Guid PayorId, string? PayerName, string SpaceNumber,
    FollowUpScopeDto Scope, DateOnly PeriodStart, decimal Assessed, decimal Collected, decimal Outstanding,
    string Priority, LotRentalEvent? Event, string Link);
