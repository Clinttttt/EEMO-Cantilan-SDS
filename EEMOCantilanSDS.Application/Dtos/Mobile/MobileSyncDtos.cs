using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>Which facility collection an offline operation represents.</summary>
public enum OfflineOperationKind
{
    NpmDaily = 1,
    MonthlyRental = 2,
    Slaughter = 3,
    Trip = 4,
    TpmVendor = 5,
    NpmUtility = 6,
    WcfCollection = 7,
    /// <summary>A governed configurable service (Market Fees, Landing/Berthing, Transfer Large Cattle, Vegetable/Fruit).</summary>
    GovernedService = 8,
    /// <summary>A canonical ECF (Electricity) collection on an already-canonical source: amount only, no physical serial (IA-062).</summary>
    EcfCollection = 9,
    /// <summary>A canonical monthly-rent collection on an already-canonical PaymentRecord: amount only, no physical serial (IA-062).</summary>
    RentCollection = 10,
    /// <summary>A canonical Tabo (vendor market-day) or Slaughterhouse (approved animal x heads) collection: facts only, the amount is the office's existing fee schedule, no physical serial (IA-062).</summary>
    FeeScheduleCollection = 11,
    /// <summary>A canonical Fish / Meat Vendor Fee collection against one period of an existing obligation account: facts only, no physical serial (IA-050/IA-062).</summary>
    ObligationCollection = 12,
    NpmWholePayment = 13
}

/// <summary>Outcome of replaying one offline operation. Synced = persisted; Rejected = a terminal
/// business/validation failure (e.g. duplicate OR, conflict) the client must surface for review;
/// Failed = a transient error (retry on the next sync).</summary>
public enum SyncResultStatus
{
    Synced = 1,
    Rejected = 2,
    Failed = 3,
    ReconciliationRequired = 4
}

/// <summary>
/// One queued offline collection to replay. <see cref="ClientOperationId"/> is the device-generated
/// idempotency key; <see cref="BusinessDate"/> is the offline collection date (PH). Payload fields are
/// read according to <see cref="Kind"/>; unused fields are null.
/// </summary>
public sealed record SyncOfflineOperationDto(
    Guid ClientOperationId,
    OfflineOperationKind Kind,
    DateOnly BusinessDate,
    string? ORNumber = null,
    // NPM daily
    Guid? StallId = null,
    bool? IsPaid = null,
    decimal? FishKilos = null,
    // Monthly rental (TCC/NCC/BBQ/ICE)
    PaymentStatus? Status = null,
    decimal? PartialAmount = null,
    // Slaughterhouse
    string? OwnerName = null,
    AnimalType? AnimalType = null,
    string? CustomAnimalType = null,
    int? NumberOfHeads = null,
    decimal? CustomRate = null,
    // Transport terminal trip
    Guid? TransporterId = null,
    string? DriverName = null,
    string? PlateNumber = null,
    string? Route = null,
    string? Organization = null,
    DateTime? OccurredAt = null,   // offline UTC timestamp for the trip
    // Tabo-an vendor
    string? VendorName = null,
    string? Goods = null,
    // common
    string? Remarks = null,
    // NPM daily: excused/absent day (₱0 owed, mutually exclusive with IsPaid)
    bool? IsAbsent = null,
    // NPM utility bill payment (electricity + water settled independently)
    Guid? UtilityBillId = null,
    PaymentStatus? ElecStatus = null,
    decimal? ElecPartialAmount = null,
    PaymentStatus? WaterStatus = null,
    decimal? WaterPartialAmount = null,
    string? ElecORNumber = null,
    string? WaterORNumber = null,
    int PayloadVersion = 0,
    decimal? ReceivedAmount = null,
    long? WaterSourceVersion = null,
    Guid? AccountableDocumentId = null,
    string? DocumentNumber = null,
    DateTime? IssuedAtUtc = null,
    decimal? MeatKilos = null,
    // Governed configurable service (Kind = GovernedService): the collector states facts; the server resolves the rest.
    string? OperationCode = null,
    GovernedServiceMode? CollectionMode = null,
    string? PayerName = null,
    string? Reference = null,
    string? VehicleClassCode = null,
    // WCF direct entry: the billing period of the selected source (StallId above identifies the source).
    int? BillingYear = null,
    int? BillingMonth = null,
    // Approved fee option services (e.g. Market Fees): the fee option the collector selected. The amount rule is the server's.
    Guid? FeeOptionId = null,
    // Canonical ECF collection (Kind = EcfCollection): the Electricity source version the collector was shown (UtilityBillId above is the source).
    long? ElectricitySourceVersion = null,
    // Canonical rent collection (Kind = RentCollection): the PaymentRecord settlement version shown to the collector (StallId + BillingYear/Month identify the source).
    long? RentSourceVersion = null,
    // Canonical Fish / Meat Vendor Fee (Kind = ObligationCollection): the obligation account; BillingYear/BillingMonth name the period.
    Guid? ObligationAccountId = null,
    string? NpmQuoteToken = null);

public sealed record SyncOperationResultDto(
    Guid ClientOperationId,
    SyncResultStatus Status,
    string? Message,
    // The server-allocated StallTrack Reference Code of the posted Collection (null while waiting or when rejected).
    string? ReferenceCode = null,
    Guid? CollectionId = null);

public sealed record SyncOfflineCollectionsResultDto(
    int SyncedCount,
    int RejectedCount,
    int FailedCount,
    IReadOnlyList<SyncOperationResultDto> Results,
    int ReconciliationRequiredCount = 0);
