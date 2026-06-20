using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Mobile.Models;

/// <summary>Local lifecycle state of a queued offline collection on the device.</summary>
public enum PendingLocalStatus
{
    /// <summary>Captured offline (or queued for resilience), not yet successfully synced. Will be (re)tried.</summary>
    Pending = 0,

    /// <summary>Server confirmed it was persisted (idempotent). Removed from the queue after sync.</summary>
    Synced = 1,

    /// <summary>Terminal business/validation failure (e.g. duplicate OR). Needs the collector to fix —
    /// never auto-retried.</summary>
    Rejected = 2,

    /// <summary>Transient failure (server/network). Stays queued and is retried on the next sync.</summary>
    Failed = 3
}

/// <summary>
/// A single collection captured on the device while offline. Mirrors
/// <see cref="SyncOfflineOperationDto"/> field-for-field (so it can replay through the existing
/// <c>POST /api/Mobile/sync</c> endpoint), plus local-only metadata (<see cref="LocalStatus"/>,
/// <see cref="ResultMessage"/>, <see cref="CreatedAt"/>) and display fields for the pending-review UI.
///
/// <para><see cref="ClientOperationId"/> is the device-generated idempotency key — generated once at
/// capture and reused across every retry; that is what makes the server write idempotent.</para>
/// </summary>
public sealed class PendingOperation
{
    // ── Idempotency + routing ──────────────────────────────────────────────
    public Guid ClientOperationId { get; set; } = Guid.NewGuid();
    public OfflineOperationKind Kind { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string? ORNumber { get; set; }

    /// <summary>Key of the collector who captured this op. Only this collector may sync it (prevents
    /// a different collector on the same device from syncing it under their own identity).</summary>
    public string? OwnerKey { get; set; }

    // ── NPM daily ──
    public Guid? StallId { get; set; }
    public bool? IsPaid { get; set; }
    public decimal? FishKilos { get; set; }

    // ── Monthly rental (TCC/NCC/BBQ/ICE) ──
    public PaymentStatus? Status { get; set; }
    public decimal? PartialAmount { get; set; }

    // ── Slaughterhouse ──
    public string? OwnerName { get; set; }
    public AnimalType? AnimalType { get; set; }
    public string? CustomAnimalType { get; set; }
    public int? NumberOfHeads { get; set; }
    public decimal? CustomRate { get; set; }

    // ── Transport terminal trip ──
    public Guid? TransporterId { get; set; }
    public string? DriverName { get; set; }
    public string? PlateNumber { get; set; }
    public string? Route { get; set; }
    public string? Organization { get; set; }
    public DateTime? OccurredAt { get; set; }

    // ── Tabo-an vendor ──
    public string? VendorName { get; set; }
    public string? Goods { get; set; }

    // ── Common ──
    public string? Remarks { get; set; }

    // ── Local-only metadata ────────────────────────────────────────────────
    public PendingLocalStatus LocalStatus { get; set; } = PendingLocalStatus.Pending;
    public string? ResultMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Display fields (for the pending-review sheet only) ──────────────────
    public string FacilityLabel { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    /// <summary>Projects this queued row into the wire DTO replayed by <c>POST /api/Mobile/sync</c>.</summary>
    public SyncOfflineOperationDto ToDto() => new(
        ClientOperationId,
        Kind,
        BusinessDate,
        ORNumber,
        StallId,
        IsPaid,
        FishKilos,
        Status,
        PartialAmount,
        OwnerName,
        AnimalType,
        CustomAnimalType,
        NumberOfHeads,
        CustomRate,
        TransporterId,
        DriverName,
        PlateNumber,
        Route,
        Organization,
        OccurredAt,
        VendorName,
        Goods,
        Remarks);
}
