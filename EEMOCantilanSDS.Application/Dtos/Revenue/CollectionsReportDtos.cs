using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>One classified line of a posted canonical Collection. The same SRC can appear on several rows (one per classified line).</summary>
public sealed record CollectionRegisterRowDto(
    Guid CollectionId,
    DateOnly BusinessDate,
    string ReferenceCode,
    string? DocumentNumber,
    RevenueInstrumentType? Instrument,
    string? PayorName,
    Guid? CollectorId,
    string? CollectorName,
    Guid ClassificationId,
    string ClassificationName,
    string SourceLabel,
    decimal Amount,
    decimal CorrectionEffect,
    // Posted, Corrected or Reversed. A correction never edits the line; it is shown beside it.
    string Status,
    // What the line froze about itself, when it froze anything: the approved charge's own name ("Table Charge") and, for Slaughterhouse,
    // the animal and heads ("Hog × 3"). Null for every line that carries no such snapshot - nothing is inferred.
    string? ItemLabel = null,
    string? Subject = null,
    // Optional additive field for clients deployed before this timestamp was available.
    // Recording instant, not the assessed/selected BusinessDate; used for newest-first mobile Records.
    DateTime? RecordedAtUtc = null);

/// <summary>
/// The RCD-style summary derived from the register: what was collected per classification, with the documents behind it.
/// Transactions first, report second: nothing here is typed in.
/// </summary>
public sealed record CollectionsSummaryLineDto(
    Guid ClassificationId,
    string ClassificationName,
    int CollectionCount,
    decimal Gross,
    decimal CorrectionEffect,
    decimal Net,
    string? FirstReferenceCode,
    string? LastReferenceCode);

public sealed record CollectionsRegisterDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CollectionRegisterRowDto> Rows,
    IReadOnlyList<CollectionsSummaryLineDto> Summary,
    decimal Gross,
    decimal CorrectionEffect,
    decimal Net,
    // True when the period held more rows than the register shows; the summary always covers the whole period.
    bool RowsTruncated,
    IReadOnlyList<global::EEMOCantilanSDS.Application.Common.Revenue.CollectionSessionDisplayGroup>? Sessions = null);

public sealed record CollectionDocumentLineDto(
    string ClassificationName,
    string SourceLabel,
    decimal Amount,
    decimal CorrectionEffect,
    IReadOnlyList<string> Details);

public sealed record CollectionDocumentDto(
    Guid CollectionId,
    string ReferenceCode,
    string? DocumentNumber,
    RevenueInstrumentType? Instrument,
    string? DocumentState,
    DateOnly BusinessDate,
    DateTime RecordedAtUtc,
    string? PayorName,
    string? CollectorName,
    decimal Total,
    decimal CorrectionEffect,
    IReadOnlyList<CollectionDocumentLineDto> Lines,
    Guid? RemittanceId,
    string? RemittanceStatus);

/// <summary>Traces one physical serial: its custody, the collection it was consumed by, and the remittance that covers it.</summary>
public sealed record DocumentTraceDto(
    string DocumentNumber,
    RevenueInstrumentType Instrument,
    string State,
    string? Custodian,
    string? SpoiledReason,
    CollectionDocumentDto? Collection);
