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
    string? CollectorName = null,
    // NPM only: the month was fully excused/absent (every collectable day marked absent) — ₱0 owed,
    // shown as a distinct "Absent" row rather than Unpaid.
    bool IsExcused = false
);
