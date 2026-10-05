using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

// A checkout is orchestration, never an operation or revenue classification.
public enum CollectionSessionItemKind { Water = 1, GovernedService = 2, Obligation = 3, Electricity = 4 }
public enum CollectionSessionStatus { NeedsReview = 1, Recorded = 2 }
public sealed record SessionWaterIntent(Guid StallId, int Year, int Month, Guid? UtilityBillId = null, long SourceVersion = 0);
public sealed record SessionGovernedIntent(string OperationCode, GovernedServiceMode? Mode = null,
    Guid? FeeOptionId = null, string? VehicleClassCode = null, string? Reference = null);
public sealed record SessionObligationIntent(Guid AccountId, int Year, int Month);
public sealed record SessionElectricityIntent(Guid UtilityBillId, long SourceVersion);
public sealed record CollectionSessionItemIntent(Guid ClientItemId, CollectionSessionItemKind Kind, decimal ConfirmedAmount,
    SessionWaterIntent? Water = null, SessionGovernedIntent? Service = null,
    SessionObligationIntent? Obligation = null, SessionElectricityIntent? Electricity = null);
public sealed record CollectionSessionIntent(Guid ClientCollectionSessionId, DateOnly BusinessDate, Guid? PayorId,
    IReadOnlyList<CollectionSessionItemIntent> Items);
public sealed record RecordCollectionSessionRequest(CollectionSessionIntent Intent, string? QuoteFingerprint);
public sealed record CollectionSessionProblem(Guid? ClientItemId, string Code, string Message);
public sealed record CollectionSessionItemQuote(Guid ClientItemId, CollectionSessionItemKind Kind,
    string OperationCode, string DisplayName, string Context, RevenueInstrumentType Instrument, decimal Amount,
    string SourceVersion, Guid GroupId);
public sealed record CollectionSessionInstrumentTotal(RevenueInstrumentType Instrument, decimal Amount);
public sealed record CollectionSessionQuote(Guid ClientCollectionSessionId, Guid? PayorId, DateOnly BusinessDate,
    IReadOnlyList<CollectionSessionItemQuote> Items, IReadOnlyList<CollectionSessionInstrumentTotal> InstrumentTotals,
    decimal GrandTotal, string? QuoteFingerprint, IReadOnlyList<CollectionSessionProblem> Problems)
{
    public bool CanRecord => Problems.Count == 0 && Items.Count > 0;
}
public sealed record CollectionSessionCollection(Guid CollectionId, string ReferenceCode,
    RevenueInstrumentType Instrument, decimal Amount, IReadOnlyList<Guid> ClientItemIds, string Disposition);
public sealed record CollectionSessionResult(Guid ClientCollectionSessionId, CollectionSessionStatus Status,
    Guid? PayorId, decimal GrandTotal, IReadOnlyList<CollectionSessionCollection> Collections,
    IReadOnlyList<CollectionSessionProblem> Problems, bool ExistingOutcome = false);
public sealed record CollectionSessionCapability(CollectionSessionItemKind? Kind, string OperationCode,
    string DisplayName, bool Supported, bool CanAdd, string? ReasonCode, string? Reason,
    bool RequiresPayor, IReadOnlyList<string> RequiredInputs);
public sealed record CollectionSessionDiscovery(Guid? PayorId, DateOnly BusinessDate,
    IReadOnlyList<CollectionSessionCapability> Operations,
    IReadOnlyList<WcfMobileSourceDto>? WaterSources = null,
    IReadOnlyList<EcfObligationQuoteDto>? ElectricitySources = null,
    IReadOnlyList<ObligationQuoteDto>? ObligationSources = null,
    IReadOnlyList<CollectionSessionServiceTerms>? ServiceTerms = null);
public sealed record CollectionSessionServiceTerms(GovernedServiceMode? Mode, GovernedServiceTermsDto Terms);
