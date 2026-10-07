using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>One revenue classification's contribution (net of corrections) inside a collection or a remittance scope.</summary>
public sealed record RemittanceBreakdownDto(Guid ClassificationId, string Name, decimal Amount);

/// <summary>A posted Collection that a remittance may cover, with what it is worth net of any correction.</summary>
public sealed record RemittanceCollectionDto(
    Guid CollectionId,
    DateOnly BusinessDate,
    string ReferenceCode,
    RevenueInstrumentType? Instrument,
    string? PayerName,
    decimal NetAmount,
    IReadOnlyList<RemittanceBreakdownDto> Lines,
    bool CorrectedAfterRemittance = false);

/// <summary>What is collectable into one remittance for a collector and period, derived from posted Collections only.</summary>
public sealed record RemittanceScopeDto(
    Guid CollectorId,
    DateOnly From,
    DateOnly To,
    RevenueInstrumentType? Instrument,
    IReadOnlyList<RemittanceCollectionDto> Collections,
    decimal ExpectedAmount,
    IReadOnlyList<RemittanceBreakdownDto> Breakdown,
    string? FirstReferenceCode,
    string? LastReferenceCode);

public sealed record RecordRemittanceRequest(
    Guid ClientOperationId,
    Guid CollectorId,
    DateOnly RemittanceDate,
    DateOnly From,
    DateOnly To,
    RevenueInstrumentType? Instrument,
    IReadOnlyList<Guid>? CollectionIds,
    decimal AmountRemitted,
    string? Reference,
    string? Remarks);

/// <summary>Several collectors' remittances recorded together; each stays its own record with its own operation identity.</summary>
public sealed record RecordRemittanceBatchRequest(IReadOnlyList<RecordRemittanceRequest> Remittances);
public sealed record RemittanceReviewRequest(DateOnly From, DateOnly To, IReadOnlyList<Guid>? CollectorIds = null,
    RevenueInstrumentType? Instrument = null);
public sealed record RemittanceReviewDto(IReadOnlyList<RemittanceScopeDto> Collectors, decimal Total, int CollectionCount);

public sealed record VoidRemittanceRequest(string Reason);

public sealed record RemittanceRowDto(
    Guid Id,
    DateOnly RemittanceDate,
    Guid CollectorId,
    string CollectorName,
    RevenueInstrumentType? Instrument,
    int CollectionCount,
    decimal ExpectedAmount,
    decimal RemittedAmount,
    decimal DifferenceAmount,
    string? Reference,
    RemittanceStatus Status,
    DateTime RecordedAtUtc,
    Guid? SubmissionId = null);

/// <summary>One collector's independent remittance inside a History row.</summary>
public sealed record RemittanceHistoryMemberDto(
    Guid RemittanceId, Guid CollectorId, string CollectorName, int CollectionCount,
    decimal ExpectedAmount, decimal RemittedAmount, decimal DifferenceAmount, RemittanceStatus Status);

/// <summary>
/// One History row: a single remittance, or the remittances saved together from one multi-collector submission. Members stay
/// separate financial records; the row's figures are only their sums. Members are in the order the submission was made.
/// </summary>
public sealed record RemittanceHistoryRowDto(
    Guid? SubmissionId,
    Guid PrimaryRemittanceId,
    DateOnly RemittanceDate,
    RevenueInstrumentType? Instrument,
    int CollectionCount,
    decimal ExpectedAmount,
    decimal RemittedAmount,
    decimal DifferenceAmount,
    string StatusLabel,
    DateTime RecordedAtUtc,
    string? Reference,
    IReadOnlyList<RemittanceHistoryMemberDto> Members);

/// <summary>A multi-collector submission's report: every collector's own remittance in submission order, and the combined totals.</summary>
public sealed record RemittanceSubmissionDto(
    Guid SubmissionId,
    DateOnly RemittanceDate,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    RevenueInstrumentType? Instrument,
    string RecordedBy,
    int CollectionCount,
    decimal ExpectedAmount,
    decimal RemittedAmount,
    decimal DifferenceAmount,
    string StatusLabel,
    IReadOnlyList<RemittanceDetailDto> Remittances);

public sealed record RemittanceDetailDto(
    RemittanceRowDto Row,
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    string? Remarks,
    string RecordedBy,
    string? VoidReason,
    string? VoidedBy,
    DateTime? VoidedAtUtc,
    IReadOnlyList<RemittanceCollectionDto> Collections,
    IReadOnlyList<RemittanceBreakdownDto> Breakdown,
    string? FirstReferenceCode,
    string? LastReferenceCode,
    bool NeedsReview);

/// <summary>Physical form accountability for one collector and instrument. These are counts of forms, never pesos.</summary>
public sealed record FormAccountabilityDto(
    RevenueInstrumentType Instrument,
    int Assigned,
    int Issued,
    int Spoiled,
    int Returned,
    int NeedsReview,
    int OnHand,
    int Lost = 0)
{
    public int AccountedFor => Issued + Spoiled + Returned + NeedsReview + OnHand + Lost;
}

/// <summary>A collector's money and forms position for a period: collected, remitted and unremitted are pesos; forms are counts.</summary>
public sealed record CollectorPositionDto(
    Guid CollectorId,
    string CollectorName,
    decimal Collected,
    decimal Remitted,
    decimal Unremitted,
    int NeedsReviewCount,
    decimal LegacyCollectionsOutsideRemittance,
    IReadOnlyList<FormAccountabilityDto> Forms);

public sealed record AccountabilityPositionDto(
    DateOnly From,
    DateOnly To,
    decimal Collected,
    decimal Remitted,
    decimal Unremitted,
    int NeedsReviewCount,
    decimal LegacyCollectionsOutsideRemittance,
    IReadOnlyList<CollectorPositionDto> Collectors);

public sealed record ReturnUnusedFormsRequest(Guid FormBookId, long FirstSerialNumber, long LastSerialNumber);

public sealed record SpoilFormRequest(Guid AccountableDocumentId, string Reason, string? Note);

public sealed record SpoiledFormDto(
    Guid AccountableDocumentId, string DocumentNumber, RevenueInstrumentType Instrument, string Reason, string? Note,
    string ActorName, DateTime RecordedAtUtc, Guid? CustodianUserId);

/// <summary>
/// One posted Collection the signed-in collector took, net of corrections, with its classification lines. The named Payor is
/// read only through the Collection's explicit Payor link; the frozen payer text is evidence and never an identity.
/// </summary>
public sealed record CollectorCollectionFactDto(
    Guid CollectionId,
    DateOnly BusinessDate,
    string ReferenceCode,
    RevenueInstrumentType? Instrument,
    Guid? PayorId,
    string? PayorName,
    decimal NetAmount,
    IReadOnlyList<RemittanceBreakdownDto> Lines);
