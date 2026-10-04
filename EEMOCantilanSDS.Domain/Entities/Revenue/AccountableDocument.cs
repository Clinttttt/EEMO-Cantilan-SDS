using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>One physical accountable-form unit. Its identity and consumed state are never reused.</summary>
public sealed class AccountableDocument : AuditableEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid FormBookId { get; private set; }
    public RevenueInstrumentType InstrumentType { get; private set; }
    public long SerialNumber { get; private set; }
    public string DocumentNumber { get; private set; } = string.Empty;
    /// <summary>Lookup key of the printed number: upper-cased, whitespace removed. Detects "2315601 A" vs "2315601A"; the printed DocumentNumber stays authoritative.</summary>
    public string NormalizedNumber { get; private set; } = string.Empty;
    public AccountableDocumentState State { get; private set; } = AccountableDocumentState.InOffice;
    public Guid? AssignedUserId { get; private set; }
    public Guid? CollectionId { get; private set; }
    public Guid? ClientOperationId { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }

    private AccountableDocument() { }

    public static AccountableDocument Register(AccountableFormBook book, long serialNumber, string createdBy)
    {
        ArgumentNullException.ThrowIfNull(book);
        var number = book.FormatNumber(serialNumber);
        return new AccountableDocument
        {
            Id = Guid.NewGuid(), MunicipalityId = book.MunicipalityId, FormBookId = book.Id,
            InstrumentType = book.InstrumentType, SerialNumber = serialNumber, DocumentNumber = number,
            NormalizedNumber = AccountableSerial.Normalize(number),
            State = AccountableDocumentState.InOffice, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy
        };
    }

    public void AssignTo(Guid userId, string updatedBy)
    {
        if (State != AccountableDocumentState.InOffice || userId == Guid.Empty)
            throw new InvalidOperationException("Only an in-office unused document can be assigned to a valid user.");
        State = AccountableDocumentState.Assigned;
        AssignedUserId = userId;
        Touch(updatedBy);
    }

    /// <summary>
    /// Moves an assigned, still-unused document from one collector's custody to another's. Only custody changes: an
    /// issued, consumed, spoiled or awaiting-review document is never transferred, so consumed history is untouched.
    /// </summary>
    public void TransferTo(Guid userId, string updatedBy)
    {
        if (State != AccountableDocumentState.Assigned || userId == Guid.Empty || userId == AssignedUserId)
            throw new InvalidOperationException("Only an assigned unused document can move to a different valid collector.");
        AssignedUserId = userId;
        Touch(updatedBy);
    }

    public void ReturnToOffice(string updatedBy)
    {
        if (State != AccountableDocumentState.Assigned)
            throw new InvalidOperationException("Only an assigned unused document can return to office custody.");
        State = AccountableDocumentState.InOffice;
        AssignedUserId = null;
        Touch(updatedBy);
    }

    public void Consume(Guid? collectionId, Guid clientOperationId, DateTime consumedAtUtc, string updatedBy)
    {
        if (State is not (AccountableDocumentState.InOffice or AccountableDocumentState.Assigned))
            throw new InvalidOperationException("This accountable document is already consumed or unavailable.");
        if (collectionId == Guid.Empty || clientOperationId == Guid.Empty)
            throw new ArgumentException("Operation identity is required and Collection identity must be valid when supplied.");
        if (consumedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Consumption time must be UTC.", nameof(consumedAtUtc));
        State = AccountableDocumentState.Consumed;
        CollectionId = collectionId;
        ClientOperationId = clientOperationId;
        ConsumedAtUtc = consumedAtUtc;
        AssignedUserId = null;
        Touch(updatedBy);
    }

    public void LinkSyncedCollection(Guid collectionId, Guid clientOperationId, string updatedBy)
    {
        if (State != AccountableDocumentState.ReconciliationRequired
            || CollectionId.HasValue || collectionId == Guid.Empty
            || ClientOperationId != clientOperationId)
            throw new InvalidOperationException("Only the original issued operation can resolve this document exception.");
        CollectionId = collectionId;
        State = AccountableDocumentState.Consumed;
        Touch(updatedBy);
    }

    public void MarkSyncIssue(string updatedBy)
    {
        if (State != AccountableDocumentState.Consumed)
            throw new InvalidOperationException("Only an issued and consumed document can become a sync exception.");
        State = AccountableDocumentState.ReconciliationRequired;
        Touch(updatedBy);
    }
    /// <summary>
    /// Permanently records a collector's physical issue of an assigned document when canonical
    /// posting cannot be accepted. The ticket stays bound to its original operation and is never
    /// returned to office inventory. AssignedUserId is retained as custody evidence.
    /// </summary>
    public void MarkPhysicalIssueReconciliationRequired(
        Guid clientOperationId, DateTime issuedAtUtc, string updatedBy)
    {
        if (State is not (AccountableDocumentState.Assigned or AccountableDocumentState.InOffice))
            throw new InvalidOperationException("Only an unused ticket can be recorded as physically issued.");
        if (clientOperationId == Guid.Empty)
            throw new ArgumentException("Client operation id is required.", nameof(clientOperationId));
        if (issuedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Physical issue timestamp must be UTC.", nameof(issuedAtUtc));
        State = AccountableDocumentState.ReconciliationRequired;
        ClientOperationId = clientOperationId;
        ConsumedAtUtc = issuedAtUtc;
        Touch(updatedBy);
    }

    /// <summary>
    /// Blocks an unused unit for good because it was reported lost. Only a blank unit changes state; an issued unit keeps its
    /// financial state and its loss is a recorded exception only. A lost unit never returns to stock.
    /// </summary>
    public void MarkLost(string updatedBy)
    {
        if (State is not (AccountableDocumentState.InOffice or AccountableDocumentState.Assigned))
            throw new InvalidOperationException("Only an unused document can be blocked as lost.");
        State = AccountableDocumentState.Lost;
        AssignedUserId = null;
        Touch(updatedBy);
    }

    public void Void(string updatedBy)
    {
        if (State is AccountableDocumentState.Lost or AccountableDocumentState.Voided)
            throw new InvalidOperationException("A lost or already cancelled document stays as recorded.");
        if (State is AccountableDocumentState.Consumed or AccountableDocumentState.ReconciliationRequired)
            throw new InvalidOperationException("An issued document requires linked correction history and remains consumed.");
        State = AccountableDocumentState.Voided;
        AssignedUserId = null;
        Touch(updatedBy);
    }

    private void Touch(string updatedBy)
    {
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}
