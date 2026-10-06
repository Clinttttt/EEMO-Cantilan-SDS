using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using System.Text.Json.Serialization;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

// A checkout is orchestration, never an operation or revenue classification.
public enum CollectionSessionItemKind { Water = 1, GovernedService = 2, Obligation = 3, Electricity = 4, Weighing = 5, Slaughter = 6, VendorFee = 7, NpmWholePayment = 8 }
public enum CollectionSessionStatus { NeedsReview = 1, Recorded = 2 }
public sealed record SessionWaterIntent(Guid StallId, int Year, int Month, Guid? UtilityBillId = null, long SourceVersion = 0);
public sealed record SessionGovernedIntent(string OperationCode, GovernedServiceMode? Mode = null,
    Guid? FeeOptionId = null, string? VehicleClassCode = null, string? Reference = null);
public sealed record SessionObligationIntent(Guid AccountId, int Year, int Month);
public sealed record SessionElectricityIntent(Guid UtilityBillId, long SourceVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? StallId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Year = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Month = null);
public enum WeighingType { Fish = 1, Meat = 2 }
public sealed record SessionWeighingIntent(Guid StallId, WeighingType Type, decimal Kilograms);
public sealed record SessionSlaughterIntent(AnimalType Animal, int Heads, string? CustomAnimalName = null, string? OwnerName = null);
public sealed record SessionVendorFeeIntent(Guid StallId);
public sealed record SessionNpmWholeIntent(Guid StallId, int Year, int Month);
public sealed record SessionSlaughterOption(AnimalType Animal, string Name, string? CustomAnimalName,
    RevenueInstrumentType? Instrument = null);
public sealed record WeighingSourceDto(Guid StallId, string StallNo, Guid? PayorId, string PayerName, string Context,
    Guid? OccupancyId = null, MarketSection? Section = null);
public sealed record WeighingRateDto(WeighingType Type, decimal RatePerKilo, DateOnly EffectiveDate, Guid? RateId = null);
public sealed record SessionNpmWholeSource(Guid StallId, Guid OccupancyId, Guid PayorId, string StallNo,
    string PayerName, int Year, int Month, RevenueInstrumentType Instrument, decimal RemainingAmount,
    decimal? MonthlyObligation, decimal? CollectedAmount, decimal? Credits);
public sealed record CollectionSessionItemIntent(Guid ClientItemId, CollectionSessionItemKind Kind, decimal ConfirmedAmount,
    SessionWaterIntent? Water = null, SessionGovernedIntent? Service = null,
    SessionObligationIntent? Obligation = null, SessionElectricityIntent? Electricity = null, SessionWeighingIntent? Weighing = null,
    SessionSlaughterIntent? Slaughter = null, SessionVendorFeeIntent? VendorFee = null, SessionNpmWholeIntent? NpmWhole = null);
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
    bool RequiresPayor, IReadOnlyList<string> RequiredInputs, bool StandaloneAvailable = false,
    IReadOnlyList<CollectionSessionSourceChoice>? Choices = null)
{
    public int EligibleChoiceCount => Choices?.Count ?? 0;
    public bool CanAutoSelect => CanAdd && EligibleChoiceCount == 1;
}
public sealed record CollectionSessionDiscovery(Guid? PayorId, DateOnly BusinessDate,
    IReadOnlyList<CollectionSessionCapability> Operations,
    IReadOnlyList<WcfMobileSourceDto>? WaterSources = null,
    IReadOnlyList<EcfObligationQuoteDto>? ElectricitySources = null,
    IReadOnlyList<ObligationQuoteDto>? ObligationSources = null,
    IReadOnlyList<CollectionSessionServiceTerms>? ServiceTerms = null,
    IReadOnlyList<WeighingSourceDto>? WeighingSources = null, IReadOnlyList<WeighingRateDto>? WeighingRates = null,
    IReadOnlyList<SessionSlaughterOption>? SlaughterOptions = null, string? PayorDisplayName = null,
    IReadOnlyList<DirectVendorFeeSource>? VendorFeeSources = null, IReadOnlyList<WeighingSourceDto>? NpmSources = null,
    IReadOnlyList<SessionNpmWholeSource>? NpmWholeSources = null);
public sealed record CollectionSessionServiceTerms(GovernedServiceMode? Mode, GovernedServiceTermsDto Terms);

// Display/selection facts only. A choice is not a reviewed quote and grants no posting authority.
public enum CollectionSessionAmountRule { DirectAmount = 1, PreparedBalance = 2, FixedAmount = 3, QuantityRate = 4, MonthlyRemaining = 5 }
public sealed record CollectionSessionChoiceIdentity(Guid? StallId = null, Guid? OccupancyId = null,
    Guid? UtilityBillId = null, long SourceVersion = 0, int? Year = null, int? Month = null,
    Guid? FeeOptionId = null, string? VehicleClassCode = null, GovernedServiceMode? Mode = null,
    WeighingType? WeighingType = null, AnimalType? Animal = null, string? CustomAnimalName = null, Guid? AccountId = null,
    DateOnly? PeriodStart = null, LotRentalEvent? Event = null);
public sealed record CollectionSessionSourceChoice(string SelectionKey, CollectionSessionItemKind Kind,
    string OperationCode, string DisplayName, string Context, CollectionSessionChoiceIdentity Identity,
    RevenueInstrumentType Instrument, CollectionSessionAmountRule AmountRule, decimal? ServerAmount = null,
    decimal? MaximumAmount = null, decimal? Rate = null, DateOnly? RateEffectiveDate = null, Guid? RateId = null,
    IReadOnlyList<string>? RequiredInputs = null)
{
    public bool CanEnterAmount => AmountRule is CollectionSessionAmountRule.DirectAmount or CollectionSessionAmountRule.PreparedBalance;
}
