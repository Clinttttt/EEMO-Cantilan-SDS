using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
namespace EEMOCantilanSDS.Application.Dtos.Revenue;

public sealed record FineAccountRef(FineAccountKind Kind, Guid AccountId);
public sealed record FineAccountOption(FineAccountRef Account, string Occupant, string Unit, string Source, bool Closed, FacilityCode? Facility = null,
    string? Area = null, DateOnly? StartsOn = null, DateOnly? EndsOn = null, string? Lifecycle = null);
public sealed record FinePeriodRef(FineAffectedPeriodKind Kind, DateOnly Start, DateOnly End, Guid? ObligationPeriodId = null);
public sealed record FinePeriodOption(FinePeriodRef Reference, string Label);
public sealed record FineSourceOption(string Label, FineAccountKind Kind, FacilityCode? Facility);
public sealed record FinePolicyRequest(Guid PenaltyVersionId, FineAccountKind AccountKind, FacilityCode? Facility,
    string GoverningReference, FinePenaltyDateBasis DateBasis, bool AllowsUncappedAmount, bool AllowsClosedAccount);
public sealed record FinePolicyDto(Guid Id, FinePolicyRequest Evidence, PenaltyDefinitionDto? Definition = null);
public sealed record CreateFineAssessmentRequest(Guid ClientOperationId, FineAccountRef Account, Guid PolicyId,
    DateOnly ViolationDate, string? IncidentReference, string Description, decimal Amount, bool ConfirmPossibleDuplicate = false,
    FinePeriodRef? RelatedPeriod = null);
public sealed record FineAssessmentDto(Guid Id, FineAccountRef Account, string Occupant, string Unit, string Source,
    string Penalty, string Description, DateOnly ViolationDate, DateOnly AssessmentDate, string? IncidentReference,
    decimal Assessed, decimal Settled, decimal Remaining, string Status, long Version, string GoverningReference,
    DateTime RecordedAtUtc, string? DuplicateWarning, FinePeriodOption? RelatedPeriod = null);
public sealed record FineAssessmentEventDto(Guid Id, string Kind, decimal Previous, decimal Resulting, Guid ActorId,
    DateTime RecordedAtUtc, string Evidence);
public sealed record FineSettlementQuoteDto(Guid AssessmentId, FineAccountRef Account, string Occupant, string Source, string Unit,
    decimal Assessed, decimal Settled, decimal Remaining, string Status, long Version, string QuoteToken,
    RevenueInstrumentType Instrument, bool CanSettle, string? IneligibleReason);
public sealed record AddFineAssessmentDraftRequest(Guid AssessmentId, string QuoteToken, decimal Amount, long? ExpectedRevision = null);

public sealed record FineSettlementHistoryDto(Guid CollectionId, string Src, DateOnly BusinessDate,
    decimal AllocatedAmount, decimal EffectiveAmount);
