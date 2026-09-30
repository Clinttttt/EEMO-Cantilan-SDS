using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>An exact tenant-owned source identity; UtilityBill requires one independent source part.</summary>
public sealed record SettlementCutoverScope(
    CollectionSourceKind SourceKind,
    Guid SourceId,
    CollectionSourcePart? SourcePart);

/// <summary>Operator-collected capability and queue-drain evidence for one collector authorized at the source facility.</summary>
public sealed record CutoverCollectorEvidence(
    Guid CollectorId,
    string ApplicationVersion,
    int WcfPayloadVersion,
    bool LegacyQueueDrained,
    DateTime VerifiedAtUtc,
    string EvidenceReference);

/// <summary>
/// External reconciliation evidence that cannot be inferred from server rows, especially device-local offline queues.
/// It is captured inside the immutable cutover evidence; it never overrides server-detected blockers.
/// </summary>
public sealed record SettlementCutoverReconciliationEvidence(
    bool LegacyWritersQuiesced,
    bool MobileQueuesDrained,
    bool NoUnregisteredFieldDevices,
    bool OnlinePaymentsDrained,
    bool AccountableDocumentInventoryReconciled,
    bool ReportingPathVerified,
    string EvidenceReference,
    IReadOnlyList<CutoverCollectorEvidence> CollectorEvidence);

public sealed record SettlementCutoverReadinessRequest(
    SettlementCutoverScope Scope,
    SettlementCutoverReconciliationEvidence? Evidence = null);

public sealed record SettlementCutoverFreezeRequest(
    SettlementCutoverScope Scope,
    long ExpectedSourceVersion,
    string ExpectedReadinessFingerprint,
    SettlementCutoverReconciliationEvidence Evidence);

public sealed record SettlementCutoverExceptionDto(
    string EvidenceKind,
    Guid? ClientOperationId,
    Guid? AccountableDocumentId,
    string? DocumentNumber,
    Guid? CollectorId,
    string OutcomeCode,
    DateTime RecordedAtUtc);

public sealed record SettlementCutoverReadinessDto(
    SettlementCutoverScope Scope,
    string SourceLabel,
    SettlementAuthority Authority,
    long SourceVersion,
    decimal AssessmentAmount,
    decimal? LegacySettledEvidence,
    decimal? OutstandingAmount,
    RevenueInstrumentType? RequiredInstrument,
    int? UnresolvedOnlinePayments,
    int ReconciliationOperations,
    int ReconciliationDocuments,
    IReadOnlyList<SettlementCutoverExceptionDto> ReconciliationExceptions,
    int ActiveAssignedDocuments,
    IReadOnlyList<Guid> AffectedCollectors,
    IReadOnlyList<Guid> CollectorsMissingCapabilityEvidence,
    int RequiredMobilePayloadVersion,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<string> Warnings,
    bool Ready,
    string ReadinessFingerprint);

public sealed record SettlementCutoverOutcomeDto(
    SettlementCutoverScope Scope,
    SettlementAuthority Authority,
    Guid? CutoverId,
    long SourceVersion,
    decimal? OpeningAssessmentAmount,
    decimal? OpeningLegacySettledAmount,
    decimal? OpeningOutstandingAmount,
    DateTime? RecordedAtUtc,
    string Message);
