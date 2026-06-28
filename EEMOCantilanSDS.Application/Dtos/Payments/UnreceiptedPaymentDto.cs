using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payments;

/// <summary>
/// A fully-paid record whose money is in but whose Official Receipt has not been encoded — the
/// cash/field counterpart of the online "awaiting OR" queue. Monthly records are one per stall
/// (<see cref="IsDaily"/> = false); NPM daily collections are grouped per stall with
/// <see cref="Count"/> = days missing an OR. Online payments are excluded (they have their own queue).
/// </summary>
public record UnreceiptedPaymentDto(
    FacilityCode Facility,
    string StallNo,
    string Occupant,
    decimal Amount,
    int Count,
    bool IsDaily,
    Guid StallId = default
);
