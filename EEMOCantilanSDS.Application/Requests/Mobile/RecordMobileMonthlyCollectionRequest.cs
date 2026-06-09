using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record RecordMobileMonthlyCollectionRequest(
    Guid StallId,
    PaymentStatus Status,
    decimal? PartialAmount = null,
    string? ORNumber = null);
