using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>IA-044 setup lifecycle: only <see cref="Active"/> may produce a Collection.</summary>
public enum GovernedServiceSetupState
{
    SetupRequired = 1,
    Active = 2,
    Disabled = 3
}

/// <summary>
/// The transaction mode of a mode-aware service (Vegetable / Fruit, IA-046). The collector states the mode; the OR or
/// CT instrument is resolved server-side from the approved contextual policy and is never chosen by the collector.
/// </summary>
public enum GovernedServiceMode
{
    WholePayment = 1,
    DailyTransaction = 2
}

/// <summary>The instrument the approved policy resolves for one mode (null mode = the service's only context).</summary>
public sealed record GovernedServiceInstrumentDto(
    GovernedServiceMode? Mode, RevenueInstrumentType? Instrument, string? PolicyName);

public sealed record GovernedServiceDefinitionDto(
    string OperationCode,
    string Name,
    string ClassificationCode,
    bool ModeAware,
    IReadOnlyList<GovernedServiceBasis> AllowedBases,
    GovernedServiceSetupState State,
    GovernedServiceBasis? Basis,
    decimal? FixedAmount,
    decimal? MaximumAmount,
    bool MobileEnabled,
    DateOnly? EffectiveDate,
    IReadOnlyList<GovernedServiceInstrumentDto> Instruments,
    IReadOnlyList<string> SetupIssues);

/// <summary>A new effective-dated setup version. It never edits history.</summary>
public sealed record ConfigureGovernedServiceRequest(
    DateOnly EffectiveDate, GovernedServiceBasis Basis, decimal? FixedAmount, decimal? MaximumAmount,
    bool IsEnabled, bool MobileEnabled);

/// <summary>
/// Versioned received-money intent for one physically issued OR or Cash Ticket. The collector supplies facts only:
/// the amount is validated against approved setup, and the classification, instrument and rate are server-resolved.
/// </summary>
public sealed record GovernedServicePostRequest(
    int SchemaVersion, Guid ClientOperationId, string OperationCode, DateOnly BusinessDate,
    decimal ReceivedAmount, GovernedServiceMode? Mode, string? PayerName, string? Reference,
    Guid AccountableDocumentId, string DocumentNumber, DateTime? IssuedAtUtc);

public sealed record GovernedServiceOutcomeDto(
    Guid CollectionId, Guid AccountableDocumentId, string DocumentNumber, DateOnly BusinessDate,
    decimal Amount, RevenueInstrumentType Instrument, string Disposition, bool ExistingOutcome);

public sealed record GovernedServiceActivityDto(
    Guid CollectionId, DateOnly BusinessDate, DateTime RecordedAtUtc, string DocumentNumber,
    RevenueInstrumentType? Instrument, GovernedServiceMode? Mode, string? PayerName, string? Reference,
    decimal Amount, string? CollectorName, string Disposition);
