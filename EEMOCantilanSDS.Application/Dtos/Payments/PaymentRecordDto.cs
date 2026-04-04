using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payments;

public record PaymentRecordDto(
    Guid Id,
    PaymentStatus Status,
    string? ORNumber,
    decimal MonthlyRental,
    decimal? ElecAmount,
    decimal? WaterAmount,
    decimal? FishFeeAmount,
    decimal TotalPaid,
    decimal BalanceDue
);
