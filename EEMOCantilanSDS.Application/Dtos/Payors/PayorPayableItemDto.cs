using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Payors;

/// <summary>
/// A single payable monthly obligation for the payor — either an existing unpaid/partial record or a
/// synthesized current-month charge. Online payment targets the stall + (Year, Month); the record is
/// found-or-created at initiation, so no record id is needed here.
/// </summary>
public sealed record PayorPayableItemDto(
    Guid StallId,
    string StallNo,
    FacilityCode Facility,
    int Year,
    int Month,
    string Period,
    decimal BalanceDue,
    PayorPayableKind Kind = PayorPayableKind.Monthly,
    // NpmFish only: the still-uncollected, in-contract, elapsed days of the month the payor may declare
    // for, plus the resolved base fee and fish ₱/kg so the UI can preview the amount (server re-prices
    // authoritatively at initiation). Null/empty for every other kind.
    IReadOnlyList<DateOnly>? UncollectedDays = null,
    decimal? BaseFee = null,
    decimal? FishRatePerKilo = null,
    // NpmDaily only: how many days this amount is made of, and the fee for one of them. A market payor was shown a month's
    // total with no way to see that it is a day's fee counted out, which is the rule they are actually billed under.
    int? Days = null,
    decimal? DailyRate = null);
