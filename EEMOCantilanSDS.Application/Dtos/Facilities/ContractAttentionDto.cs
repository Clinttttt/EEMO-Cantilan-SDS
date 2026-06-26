using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Facilities;

/// <summary>
/// An occupied stall whose active contract is expired or expiring soon — surfaced for the Head/Admin
/// Follow-up Queue. <see cref="IsExpired"/> true = already past <see cref="ExpiryDate"/> (an active
/// occupant on a lapsed contract); false = within the warning window.
/// </summary>
public record ContractAttentionDto(
    FacilityCode FacilityCode,
    string StallNo,
    string Occupant,
    DateOnly ExpiryDate,
    bool IsExpired
);
