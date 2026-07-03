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
    IReadOnlyList<MobileSlaughterLineDto>? SlaughterLines = null);

/// <summary>One animal-type line within a slaughterhouse receipt (for the grouped record's detail view).</summary>
public sealed record MobileSlaughterLineDto(
    string AnimalType,
    int Heads,
    decimal RatePerHead,
    decimal Amount);
