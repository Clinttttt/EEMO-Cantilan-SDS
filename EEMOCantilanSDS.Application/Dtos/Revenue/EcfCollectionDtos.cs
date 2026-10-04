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

public sealed record EcfAvailableDocumentDto(Guid DocumentId, string DocumentNumber, AccountableDocumentState State, bool IsNextExpected = false);

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
    string DocumentNumber,
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
    string DocumentNumber,
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
public sealed record SelectEcfDraftDocumentRequest(long ExpectedRevision, Guid? AccountableDocumentId);
public sealed record EcfDraftRevisionRequest(long ExpectedRevision);
public sealed record PostEcfCollectionDraftRequest(long ExpectedRevision, Guid ClientOperationId);
