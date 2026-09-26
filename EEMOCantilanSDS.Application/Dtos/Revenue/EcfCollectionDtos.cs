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

public sealed record EcfAvailableDocumentDto(Guid DocumentId, string DocumentNumber, AccountableDocumentState State);

public sealed record EcfCollectionDraftLineDto(
    Guid LineId,
    decimal Amount,
    string ClassificationName,
    Guid UtilityBillId,
    CollectionSourcePart SourcePart,
    long SourceVersion,
    decimal AllocationAmount);

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
    IReadOnlyList<EcfCollectionDraftLineDto> Lines);

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
    string? CalculationSnapshot);

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

public sealed record UpdateEcfDraftAllocationRequest(long ExpectedRevision, decimal ProposedAmount);
public sealed record SelectEcfDraftDocumentRequest(long ExpectedRevision, Guid? AccountableDocumentId);
public sealed record EcfDraftRevisionRequest(long ExpectedRevision);
public sealed record PostEcfCollectionDraftRequest(long ExpectedRevision, Guid ClientOperationId);
