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
