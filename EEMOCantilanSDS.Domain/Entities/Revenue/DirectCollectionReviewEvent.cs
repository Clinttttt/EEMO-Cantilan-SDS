using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

public enum DirectCollectionReviewOutcome
{
    /// <summary>The Head has looked at the Collection and found nothing to correct.</summary>
    Confirmed = 1,
    /// <summary>The Head flagged the Collection for correction; a note says why.</summary>
    NeedsCorrection = 2,
    /// <summary>A flagged Collection has been dealt with (or the alert closed); the money itself is untouched.</summary>
    Resolved = 3
}

/// <summary>
/// One administrative review step on a posted Collection. It is append-only evidence of who looked at what and what they said; it never
/// creates, changes or removes a Collection, an SRC, an allocation or any amount, and it is not a financial disposition. The current review
/// state of a Collection is its latest event (none = awaiting review).
/// </summary>
public sealed class DirectCollectionReviewEvent : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid CollectionId { get; private set; }
    public Guid ClientOperationId { get; private set; }
    public DirectCollectionReviewOutcome Outcome { get; private set; }
    public string? Note { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = "";
    public DateTime OccurredAtUtc { get; private set; }
    private DirectCollectionReviewEvent() { }

    public static DirectCollectionReviewEvent Record(Guid tenant, Guid collection, Guid operation, DirectCollectionReviewOutcome outcome,
        string? note, Guid actorId, string actorName)
    {
        if (tenant == Guid.Empty || collection == Guid.Empty || operation == Guid.Empty || actorId == Guid.Empty)
            throw new ArgumentException("InvalidReviewIdentity");
        if (!Enum.IsDefined(outcome)) throw new ArgumentException("InvalidReviewOutcome");
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (text is { Length: > 500 }) throw new ArgumentException("ReviewNoteTooLong");
        if (outcome == DirectCollectionReviewOutcome.NeedsCorrection && text is null) throw new ArgumentException("ReviewNoteRequired");
        var now = DateTime.UtcNow;
        return new()
        {
            Id = Guid.NewGuid(), MunicipalityId = tenant, CollectionId = collection, ClientOperationId = operation, Outcome = outcome,
            Note = text, ActorId = actorId, ActorName = string.IsNullOrWhiteSpace(actorName) ? "Office User" : actorName.Trim()[..Math.Min(100, actorName.Trim().Length)],
            OccurredAtUtc = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)
        };
    }
}
