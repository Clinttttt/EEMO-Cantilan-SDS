using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payments;

public record FacilityPaymentRecordDto(
    Guid StallId,
    PaymentStatus Status,
    string? ORNumber,
    decimal TotalPaid
);
