using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payors;

/// <summary>
/// A freshly issued (or current) activation code for a stall, returned to staff so they can hand it
/// to the payor. Bound to the entered contact number and single-use until it expires.
/// </summary>
public sealed record StallActivationCodeDto(
    Guid StallId,
    string StallNo,
    FacilityCode Facility,
    string Code,
    string ContactNumber,
    DateTime ExpiresAt);
