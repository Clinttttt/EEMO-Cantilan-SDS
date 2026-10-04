using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

public sealed record EcfObligationQuoteDto(
    Guid MunicipalityId,
    Guid UtilityBillId,
    CollectionSourceKind SourceKind,
    CollectionSourcePart SourcePart,
    long ElectricitySourceVersion,
    Guid StallId,
    string StallNo,
    string FacilityName,
    string Section,
    int BillingYear,
    int BillingMonth,
    decimal PreviousReading,
    decimal CurrentReading,
    decimal Consumption,
    decimal RatePerKwh,
    decimal AssessedAmount,
    decimal CumulativeSettledEvidence,
    decimal OutstandingAmount,
    SettlementAuthority SettlementAuthority,
    Guid? PayorId,
    string? PayerNameSnapshot,
    Guid RevenueClassificationId,
    Guid RevenueClassificationPolicyId,
    string ClassificationName,
    RevenueInstrumentType Instrument,
    string ChargeBasis,
    bool CanAddToDraft,
    bool CanPostCanonical);


public sealed record EcfCollectionDraftLineDto(
    Guid LineId,
    decimal Amount,
    string ClassificationName,
    Guid UtilityBillId,
    CollectionSourcePart SourcePart,
    long SourceVersion,
    decimal AllocationAmount,
    CollectionSourceKind? SourceKind = null,
    Guid? SourceId = null,
    IReadOnlyList<CollectionDraftAllocationDto>? Allocations = null,
    string? CalculationDetail = null);

public sealed record CollectionDraftAllocationDto(
    Guid AllocationId,
    CollectionSourceKind SourceKind,
    Guid SourceId,
    CollectionSourcePart? SourcePart,
    decimal Amount,
    int? BillingYear,
    int? BillingMonth,
    string? SourceLabel,
    SettlementAuthority? SourceAuthority = null);

public sealed record RentObligationQuoteDto(
    Guid MunicipalityId,
    Guid? PaymentRecordId,
    CollectionSourceKind SourceKind,
    Guid StallId,
    string StallNo,
    string FacilityName,
    int BillingYear,
    int BillingMonth,
    Guid ContractId,
    Guid? PayorId,
    string? PayerNameSnapshot,
    decimal AssessedRentalAmount,
    decimal CumulativeSettledEvidence,
    decimal OutstandingAmount,
    SettlementAuthority SettlementAuthority,
    long SourceVersion,
    Guid RevenueClassificationId,
    Guid RevenueClassificationPolicyId,
    string ClassificationName,
    RevenueInstrumentType Instrument,
    decimal ContractRate,
    bool CanAddToDraft,
    bool CanPostCanonical,
    bool RequiresLegacyReconciliation);

public sealed record CollectionCandidateDto(
    CollectionSourceKind SourceKind,
    Guid? SourceId,
    CollectionSourcePart? SourcePart,
    Guid StallId,
    string StallNo,
    string SourceLabel,
    int BillingYear,
    int BillingMonth,
    Guid? PayorId,
    string? PayerNameSnapshot,
    decimal OutstandingAmount,
    RevenueInstrumentType Instrument,
    bool CanAddToDraft);

public sealed record CollectionPayorDto(Guid PayorId, string DisplayName);

public sealed record EcfCollectionDraftDto(
    Guid DraftId,
    long Revision,
    DateOnly BusinessDate,
    string Status,
    bool IsReviewedForCurrentRevision,
    DateTime? ReviewedAtUtc,
    string? PayerNameSnapshot,
    Guid? PayorId,
    Guid? AccountableDocumentId,
    string? DocumentNumber,
    decimal TotalAmount,
    Guid? CollectionId,
    IReadOnlyList<EcfCollectionDraftLineDto> Lines,
    bool RequiresBusinessDateRefresh = false);

public sealed record EcfPostOutcomeDto(
    Guid CollectionId,
    string ReferenceCode,
    string CurrentDisposition,
    decimal Amount,
    int ItemCount,
    bool ReturnedExistingOutcome);

public sealed record EcfCollectionActivityLineDto(
    string ClassificationName,
    decimal Amount,
    int BillingYear,
    int BillingMonth,
    string Allocation,
    string? CalculationSnapshot,
    IReadOnlyList<CollectionActivityAllocationDto>? Allocations = null,
    string? CalculationDetail = null);

public sealed record CollectionActivityAllocationDto(
    decimal Amount,
    int? BillingYear,
    int? BillingMonth,
    string SourceLabel);

public sealed record EcfCollectionActivityDto(
    Guid CollectionId,
    DateOnly BusinessDate,
    DateTime RecordedAtUtc,
    string ReferenceCode,
    string? PayerName,
    decimal TotalAmount,
    int ItemCount,
    string CurrentDisposition,
    IReadOnlyList<EcfCollectionActivityLineDto> Lines);

public sealed record CreateEcfCollectionDraftRequest(
    Guid UtilityBillId,
    decimal ProposedAmount,
    Guid? AccountableDocumentId = null,
    string? OneOffPayerName = null);

public sealed record AddEcfDraftLineRequest(Guid UtilityBillId, decimal ProposedAmount, long? ExpectedRevision = null);
public sealed record AddRentDraftAllocationRequest(
    Guid StallId,
    int BillingYear,
    int BillingMonth,
    decimal ProposedAmount,
    long? ExpectedRevision = null,
    string? OneOffPayerName = null);

public sealed record UpdateCollectionDraftAllocationRequest(
    Guid AllocationId,
    long ExpectedRevision,
    decimal ProposedAmount);

public sealed record UpdateEcfDraftAllocationRequest(long ExpectedRevision, decimal ProposedAmount);
public sealed record EcfDraftRevisionRequest(long ExpectedRevision);
public sealed record PostEcfCollectionDraftRequest(long ExpectedRevision, Guid ClientOperationId);

/// <summary>Collector Mobile ECF collection (IA-062): the collector confirms the amount of one canonical ECF source. No physical serial; the server returns the SRC.</summary>
public sealed record MobileEcfPostRequest(
    Guid ClientOperationId, Guid UtilityBillId, decimal ReceivedAmount, long ElectricitySourceVersion, DateOnly BusinessDate);

/// <summary>Collector Mobile monthly-rent collection (IA-051/IA-062) for one canonical PaymentRecord. No physical serial; the server returns the SRC.</summary>
public sealed record MobileRentPostRequest(
    Guid ClientOperationId, Guid StallId, int BillingYear, int BillingMonth, decimal ReceivedAmount, long SourceVersion, DateOnly BusinessDate);

/// <summary>
/// Collector Mobile Fish / Meat Vendor Fee collection (IA-050/IA-062) against one period of an existing obligation account. The
/// account, its approved monthly amount and its Payor are the office's own; the collector states only which account and period, and how
/// much was received (never more than the period's remaining balance). No physical serial; the server returns the SRC.
/// </summary>
public sealed record MobileObligationPostRequest(
    Guid ClientOperationId, Guid AccountId, int BillingYear, int BillingMonth, decimal ReceivedAmount, DateOnly BusinessDate);

/// <summary>One collectible Fish / Meat vendor-fee period as the collector sees it. Amounts are the server's; nothing is typed.</summary>
public sealed record MobileVendorFeeDueDto(
    Guid AccountId, string SubjectLabel, string? StallNo, string? PayorName, DateOnly PeriodStart,
    decimal AssessedAmount, decimal SettledAmount, decimal OutstandingAmount);
