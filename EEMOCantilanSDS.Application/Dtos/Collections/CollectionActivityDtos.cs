using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Collections;

/// <summary>
/// The office's unified Collection Activity for a period (IA-011, IA-050, ADR-005 §7): every real collection exactly once.
/// A legacy source row is listed only while its legacy money is authoritative (<c>CollectionSourceAuthorityMap</c>); a
/// posted canonical Collection is listed as one document event with its lines. Totals are over every event in the period,
/// whether or not <see cref="Truncated"/> cut the list.
/// </summary>
public sealed record CollectionActivityFeedDto(
    DateOnly From,
    DateOnly To,
    string DateBasis,
    string CorrectionBasis,
    DateTime GeneratedAtUtc,
    IReadOnlyList<CollectionActivityEventDto> Events,
    int EventCount,
    bool Truncated,
    decimal LegacyAmount,
    decimal CanonicalAmount,
    decimal CorrectionEffect,
    decimal NetAmount,
    IReadOnlyList<string> Notes);

/// <summary>
/// One collection/document event. <see cref="Authority"/> is "Legacy" or "Canonical". <see cref="Amount"/> is the money the
/// event recorded; <see cref="CorrectionEffect"/> is the signed effect of corrections linked to it (canonical only), and
/// <see cref="NetAmount"/> their sum. The original event is never removed by a correction.
/// </summary>
public sealed record CollectionActivityEventDto(
    string EventKey,
    string Authority,
    string Source,
    Guid? CollectionId,
    DateOnly BusinessDate,
    DateTime? RecordedAtUtc,
    string? DocumentNumber,
    RevenueInstrumentType? InstrumentType,
    IReadOnlyList<string> ReplacementDocumentNumbers,
    string? PayerName,
    Guid? PayorId,
    Guid? CollectorId,
    string? CollectorName,
    string RecordedBy,
    FacilityCode? Facility,
    string? Subject,
    string? LegacyStatus,
    decimal Amount,
    decimal CorrectionEffect,
    decimal NetAmount,
    string Disposition,
    Guid? ReplacesCollectionId,
    IReadOnlyList<CollectionActivityLineDto> Lines,
    IReadOnlyList<CollectionActivityCorrectionDto> Corrections);

/// <summary>One classified line of an event: what the money was for, and the source it settled.</summary>
public sealed record CollectionActivityLineDto(
    string ClassificationCode,
    string ClassificationName,
    decimal Amount,
    string? SourceKind,
    string? SourcePart,
    Guid? SourceId,
    int? PeriodYear,
    int? PeriodMonth,
    string? Description);

/// <summary>A correction linked to a canonical event, with its own effective and recorded moments.</summary>
public sealed record CollectionActivityCorrectionDto(
    Guid CorrectionId,
    string CorrectionType,
    DateOnly EffectiveDate,
    DateTime RecordedAtUtc,
    decimal FinancialEffect,
    Guid? ReplacementCollectionId,
    string? Reason);
