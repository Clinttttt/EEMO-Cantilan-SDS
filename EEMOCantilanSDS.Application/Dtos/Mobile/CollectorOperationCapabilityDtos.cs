namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// Whether an assigned non-facility operation can be collected NOW. Assignment alone is never collectibility: the
/// server also checks the source-specific writer, collector/facility authorization, settlement authority, approved
/// policy and accountable-document custody. Only <see cref="Ready"/> is collectible.
/// </summary>
public enum CollectorOperationCapabilityStatus
{
    NotAssigned = 0,
    /// <summary>No approved Collector Mobile writer exists for this operation.</summary>
    Unsupported = 1,
    /// <summary>Assigned, but the collector or their authorization for the current source is not active.</summary>
    AssignedButInactive = 2,
    /// <summary>No source part of this operation is under Canonical settlement authority yet.</summary>
    PendingCutover = 3,
    /// <summary>The approved classification/instrument policy is not effective for the business date.</summary>
    NeedsPolicy = 4,
    /// <summary>The collector holds no assigned accountable form of the required instrument.</summary>
    NeedsDocument = 5,
    Ready = 6
}

public sealed record CollectorOperationCapabilityDto(
    string OperationCode,
    string Name,
    bool IsAssigned,
    CollectorOperationCapabilityStatus Status,
    bool IsCollectible,
    IReadOnlyList<string> ReasonCodes);

public sealed record CollectorOperationCapabilitiesDto(
    Guid CollectorId,
    DateOnly BusinessDate,
    IReadOnlyList<CollectorOperationCapabilityDto> Operations);
