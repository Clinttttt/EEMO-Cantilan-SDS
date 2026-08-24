using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// One collection the authenticated collector recorded, across any of their assigned facilities.
/// Drives the mobile Records feed (cards). Only actual collection events appear (paid or partial) —
/// an unpaid stall has no collection event. Amounts: <see cref="Amount"/> is the full billed figure,
/// <see cref="AmountPaid"/> what was collected (equals Amount unless <see cref="IsPartial"/>).
/// </summary>
public sealed record MobileCollectorRecordDto(
    string ORNumber,
    string PayorName,
    FacilityCode FacilityCode,
    string FacilityName,
    string? StallNo,
    string Nature,
    decimal Amount,
    decimal AmountPaid,
    bool IsPartial,
    DateTime CollectedAt,
    // NPM market section (Vegetable/Fish/Meat) when applicable; FishKilos for Fish-section sales.
    MarketSection? Section = null,
    decimal? FishKilos = null,
    // True when the entry was recorded by an admin/office (CollectorId is null) rather than this
    // collector — surfaced on the mobile Records feed with an "Office" tag so attribution stays clear.
    bool IsAdminRecorded = false,
    // True for an NPM daily collection marked Absent/Excused (₱0 owed, no OR) — shown distinctly so
    // the collector's "marked absent" actions appear on the feed, never counted as a paid collection.
    bool IsAbsent = false,
    // Slaughterhouse only: the per-animal-type breakdown of the receipt. One SLH OR covers a customer's
    // whole visit, so multiple animal rows are grouped into ONE record here, with each line shown in the
    // detail popup. Null for every other facility (and for single-line receipts it still carries the one line).
    IReadOnlyList<MobileSlaughterLineDto>? SlaughterLines = null,
    // NPM only: the stall's electricity & water bill for this record's month, shown in the detail
    // (not on the card) so a payor keeps ONE card. Null when there is no utility bill for the month.
    MobileRecordUtilityDto? Utility = null,
    // The office's OWN name for an area it added itself, for a stall standing in one (Section is then null). Without it
    // the feed showed a chip for each of the three canonical areas and nothing at all for a stall in, say, "Sari Sari" —
    // the payor's own area, missing from the record of their payment.
    string? CustomSectionName = null,
    // NPM only: the day this fee is FOR, which is not the day it was collected when a payor settles what they owe. The
    // Records feed states the collection day; this lets a settled receipt name the days it covered.
    DateOnly? FeeDate = null,
    // Monthly rentals: the month the payment is FOR, e.g. "Aug 2026". Where several receipts of one payor are shown as one
    // entry, each has to name the period it answers for, or two rentals of the same amount are indistinguishable.
    string? PeriodLabel = null);

/// <summary>A stall's electricity &amp; water bill attached to an NPM collection record (detail view only).</summary>
public sealed record MobileRecordUtilityDto(
    decimal ElecCharge,
    string ElecStatus,
    decimal ElecAmountPaid,
    decimal ElecBalance,
    string? ElecORNumber,
    decimal WaterCharge,
    string WaterStatus,
    decimal WaterAmountPaid,
    decimal WaterBalance,
    string? WaterORNumber,
    decimal TotalCharge,
    decimal TotalPaid,
    decimal Balance);

/// <summary>One animal-type line within a slaughterhouse receipt (for the grouped record's detail view).</summary>
public sealed record MobileSlaughterLineDto(
    string AnimalType,
    int Heads,
    decimal RatePerHead,
    decimal Amount);
