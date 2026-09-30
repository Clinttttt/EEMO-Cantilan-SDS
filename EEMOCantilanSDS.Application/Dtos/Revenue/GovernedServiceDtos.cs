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
    Guid AccountableDocumentId, string DocumentNumber, DateTime? IssuedAtUtc,
    // Transportation / Parking only: the stable vehicle class the collector selected. The amount is that class's approved rate.
    string? VehicleClassCode = null);

public sealed record GovernedServiceOutcomeDto(
    Guid CollectionId, Guid AccountableDocumentId, string DocumentNumber, DateOnly BusinessDate,
    decimal Amount, RevenueInstrumentType Instrument, string Disposition, bool ExistingOutcome);

public sealed record GovernedServiceActivityDto(
    Guid CollectionId, DateOnly BusinessDate, DateTime RecordedAtUtc, string DocumentNumber,
    RevenueInstrumentType? Instrument, GovernedServiceMode? Mode, string? PayerName, string? Reference,
    decimal Amount, string? CollectorName, string Disposition);

/// <summary>One collection a collector took through a governed operation, as the server recorded it.</summary>
public sealed record GovernedServiceRecordDto(
    Guid CollectionId, DateOnly BusinessDate, DateTime RecordedAtUtc, string OperationCode, string OperationName,
    string DocumentNumber, RevenueInstrumentType? Instrument, GovernedServiceMode? Mode, string? PayerName,
    string? Reference, decimal Amount, string Disposition);

/// <summary>
/// What the approved setup says a collector may record today for one operation (and mode): the amount rule and the
/// instrument the policy resolves. Display facts only — posting revalidates all of it.
/// </summary>
public sealed record GovernedServiceTermsDto(
    string OperationCode, string Name, bool ModeAware, GovernedServiceBasis Basis, decimal? FixedAmount,
    decimal? MaximumAmount, RevenueInstrumentType Instrument, bool RequiresReference,
    IReadOnlyList<VehicleClassTermDto>? VehicleClasses = null);

/// <summary>One approved vehicle class and the rate in force today, for a collector to select. Display only; posting revalidates.</summary>
public sealed record VehicleClassTermDto(string Code, string Name, decimal Amount);

public sealed record VehicleClassDto(
    Guid Id, string Code, string DisplayName, bool IsActive, decimal? CurrentAmount, DateOnly? CurrentEffectiveDate);

public sealed record SaveVehicleClassRequest(string Code, string DisplayName, DateOnly EffectiveDate, decimal Amount);
