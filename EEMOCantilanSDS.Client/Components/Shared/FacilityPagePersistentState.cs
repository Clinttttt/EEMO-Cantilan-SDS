using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Components.Shared;

public sealed record FacilityPagePersistentState(
    FacilityCode FacilityCode,
    int Year,
    int Month,
    List<StallDto> Stalls,
    List<FacilityPaymentRecordDto> Payments);
