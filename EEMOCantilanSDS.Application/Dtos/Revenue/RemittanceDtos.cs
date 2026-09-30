using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>One revenue classification's contribution (net of corrections) inside a collection or a remittance scope.</summary>
public sealed record RemittanceBreakdownDto(Guid ClassificationId, string Name, decimal Amount);

/// <summary>A posted Collection that a remittance may cover, with what it is worth net of any correction.</summary>
public sealed record RemittanceCollectionDto(
    Guid CollectionId,
    DateOnly BusinessDate,
    string? DocumentNumber,
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
    string? FirstDocumentNumber,
    string? LastDocumentNumber);

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
    DateTime RecordedAtUtc);

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
    string? FirstDocumentNumber,
    string? LastDocumentNumber,
    bool NeedsReview);

/// <summary>Physical form accountability for one collector and instrument. These are counts of forms, never pesos.</summary>
public sealed record FormAccountabilityDto(
    RevenueInstrumentType Instrument,
    int Assigned,
    int Issued,
    int Spoiled,
    int Returned,
    int NeedsReview,
    int OnHand)
{
    public int AccountedFor => Issued + Spoiled + Returned + NeedsReview + OnHand;
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
