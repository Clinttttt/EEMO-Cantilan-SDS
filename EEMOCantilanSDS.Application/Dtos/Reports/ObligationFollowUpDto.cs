using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Reports;

public enum FollowUpScopeKind { Facility = 1, MonthlySpace = 2, EventLot = 3 }
public sealed record FollowUpScopeDto(FollowUpScopeKind Kind, string OperationCode, string DisplayName, FacilityCode? Facility = null);
public sealed record ObligationFollowUpItemDto(Guid AccountId, Guid PayorId, string? PayerName, string SpaceNumber,
    FollowUpScopeDto Scope, DateOnly PeriodStart, decimal Assessed, decimal Collected, decimal Outstanding,
    string Priority, LotRentalEvent? Event, string Link,
    // "delinquent" or "arrears", by the stall rules; ArrearsBasis says why ("Previous year", "Expired contract", "Ended occupancy").
    string ReasonKind = "delinquent", string? ArrearsBasis = null,
    // The period belongs to an earlier term of a renewed space (stated on the whole-time history and the Arrears register, not the live queue).
    bool EarlierTerm = false,
    // The last day of the term in force (null when held without a contract), so a never-renewed expired term can be stated as such.
    DateOnly? TermLastDay = null);
