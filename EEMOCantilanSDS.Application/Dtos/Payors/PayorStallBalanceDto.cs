using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payors;

/// <summary>
/// One linked stall's outstanding position for the payor dashboard. <see cref="OutstandingBalance"/>
/// is the sum of <c>BalanceDue</c> across the stall's unpaid/partial monthly records.
/// </summary>
public sealed record PayorStallBalanceDto(
    Guid StallId,
    string StallNo,
    FacilityCode Facility,
    string Occupant,
    decimal MonthlyRate,
    decimal OutstandingBalance,
    int UnpaidMonths,
    string? OldestUnpaidPeriod);
