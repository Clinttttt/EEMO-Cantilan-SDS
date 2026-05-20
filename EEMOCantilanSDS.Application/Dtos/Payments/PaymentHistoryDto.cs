using EEMOCantilanSDS.Domain.Enums;
using System;

namespace EEMOCantilanSDS.Application.Dtos.Payments;

public record PaymentHistoryDto(
    string Period,
    PaymentStatus Status,
    decimal TotalBill,
    decimal AmountPaid,
    decimal BalanceDue,
    string? ORNumber,
    DateTime? PaidAt,
    string? CollectorName = null
);
