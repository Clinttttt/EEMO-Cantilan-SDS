using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payors;

/// <summary>
/// One linked space's rent the payor can pay ahead online: the office's own options (the same a collector and the office have) and the months
/// already paid ahead. <see cref="PayAheadOptionsDto.HeldBy"/> is set while anything is owed, and then no month is offered.
/// </summary>
public sealed record PayorPayAheadDto(
    Guid StallId,
    string StallNo,
    FacilityCode Facility,
    string? FacilityName,
    string? FacilityShortName,
    PayAheadOptionsDto Options,
    IReadOnlyList<PayAheadMonthDto> PaidAhead);
