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
    bool IsExcused = false,
    // Display-only attribution: who recorded the collection — the field collector when present,
    // otherwise the admin/Head resolved from the audit actor. Never affects any financial value.
    string? RecordedByName = null,
    // NPM only: the days this month is made of, earliest first, each with its own fee and receipt. A daily-billed month is
    // folded into one row here because that is how the office reconciles it, but the payor pays a day at a time and could
    // not see the days behind the total, including the ones a collector took in the field. Empty for monthly facilities.
    IReadOnlyList<PaymentHistoryDayDto>? Days = null
);

/// <summary>
/// One collected day inside a daily-billed month, as the payor's own record of it.
/// </summary>
/// <param name="ORNumber">The receipt for that day, or null where the office has yet to encode one.</param>
/// <param name="RecordedByName">The collector who took it, or the office where it was recorded there. Display only.</param>
public sealed record PaymentHistoryDayDto(
    DateOnly Day,
    decimal Amount,
    string? ORNumber,
    string? RecordedByName);
