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
    NpmUtility = 6
}

/// <summary>Outcome of replaying one offline operation. Synced = persisted; Rejected = a terminal
/// business/validation failure (e.g. duplicate OR, conflict) the client must surface for review;
/// Failed = a transient error (retry on the next sync).</summary>
public enum SyncResultStatus
{
    Synced = 1,
    Rejected = 2,
    Failed = 3
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
    string? WaterORNumber = null);

public sealed record SyncOperationResultDto(
    Guid ClientOperationId,
    SyncResultStatus Status,
    string? Message);

public sealed record SyncOfflineCollectionsResultDto(
    int SyncedCount,
    int RejectedCount,
    int FailedCount,
    IReadOnlyList<SyncOperationResultDto> Results);
