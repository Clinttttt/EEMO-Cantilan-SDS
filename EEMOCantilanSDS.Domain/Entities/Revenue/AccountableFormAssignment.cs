using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>Audited custody interval for one physical accountable-form unit.</summary>
public sealed class AccountableFormAssignment : AuditableEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid AccountableDocumentId { get; private set; }
    public Guid AssignedUserId { get; private set; }
    public string AssignedByActorId { get; private set; } = string.Empty;
    public DateTime AssignedAtUtc { get; private set; }
    public DateTime? ReturnedAtUtc { get; private set; }
    public string? ReturnedByActorId { get; private set; }
    /// <summary>The collector who held the unit before an explicit transfer; null for an assignment from office stock.</summary>
    public Guid? TransferredFromUserId { get; private set; }
    /// <summary>Why the unit moved between collectors; required for a transfer, null otherwise.</summary>
    public string? TransferReason { get; private set; }

    public const int MaxReasonLength = 300;

    private AccountableFormAssignment() { }

    public static AccountableFormAssignment Assign(
        AccountableDocument document, Guid assignedUserId, string assignedByActorId,
        DateTime assignedAtUtc, string createdBy)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (assignedUserId == Guid.Empty || string.IsNullOrWhiteSpace(assignedByActorId)
            || assignedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Valid custodian, assigning actor, and UTC assignment time are required.");
        return new AccountableFormAssignment
        {
            Id = Guid.NewGuid(), MunicipalityId = document.MunicipalityId,
            AccountableDocumentId = document.Id, AssignedUserId = assignedUserId,
            AssignedByActorId = assignedByActorId.Trim(), AssignedAtUtc = assignedAtUtc,
            CreatedAt = assignedAtUtc, CreatedBy = createdBy
        };
    }

    /// <summary>
    /// Opens the receiving collector's custody interval for a unit transferred from another collector. The previous
    /// interval is closed by <see cref="RecordReturn"/> in the same save, so the history reads from → to, by, at, why.
    /// </summary>
    public static AccountableFormAssignment Transfer(
        AccountableDocument document, Guid fromUserId, Guid toUserId, string reason, string assignedByActorId,
        DateTime assignedAtUtc, string createdBy)
    {
        if (fromUserId == Guid.Empty || fromUserId == toUserId)
            throw new ArgumentException("A transfer needs a different previous custodian.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
            throw new ArgumentException($"A transfer reason of at most {MaxReasonLength} characters is required.");
        var assignment = Assign(document, toUserId, assignedByActorId, assignedAtUtc, createdBy);
        assignment.TransferredFromUserId = fromUserId;
        assignment.TransferReason = reason.Trim();
        return assignment;
    }

    public void RecordReturn(string actorId, DateTime returnedAtUtc)
    {
        if (ReturnedAtUtc.HasValue || string.IsNullOrWhiteSpace(actorId)
            || returnedAtUtc.Kind != DateTimeKind.Utc || returnedAtUtc < AssignedAtUtc)
            throw new InvalidOperationException("Custody return must be a valid, one-time event after assignment.");
        ReturnedAtUtc = returnedAtUtc;
        ReturnedByActorId = actorId.Trim();
        UpdatedAt = returnedAtUtc;
        UpdatedBy = actorId.Trim();
    }
}
