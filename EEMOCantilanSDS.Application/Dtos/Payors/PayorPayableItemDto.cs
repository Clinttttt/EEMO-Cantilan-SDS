using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payors;

/// <summary>
/// A single payable monthly obligation for the payor — either an existing unpaid/partial record or a
/// synthesized current-month charge. Online payment targets the stall + (Year, Month); the record is
/// found-or-created at initiation, so no record id is needed here.
/// </summary>
public sealed record PayorPayableItemDto(
    Guid StallId,
    string StallNo,
    FacilityCode Facility,
    int Year,
    int Month,
    string Period,
    decimal BalanceDue);
