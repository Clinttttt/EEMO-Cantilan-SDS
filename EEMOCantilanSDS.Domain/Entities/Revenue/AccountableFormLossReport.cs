using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// An append-only record that an accountable form, or specific copies of it, was lost or is missing. One report is one serial
/// (an inclusive range is recorded as one report per serial). A blank unit that is reported lost is blocked from issuance by
/// <see cref="AccountableDocument.MarkLost"/> in the same save; an issued unit keeps its financial state and this record is the
/// exception evidence. StallTrack records the event and its supporting reference; relief from accountability is an external
/// legal process and is never granted here. The external notice may be added afterwards as an
/// <see cref="AccountableFormReference"/>.
/// </summary>
public sealed class AccountableFormLossReport : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid AccountableDocumentId { get; private set; }
    public Guid? CustodianUserId { get; private set; }
    public AccountableFormCopies CopiesLost { get; private set; }
    public DateOnly LostOn { get; private set; }
    public string? Place { get; private set; }
    public string Narrative { get; private set; } = string.Empty;
    public DateOnly ReportedOn { get; private set; }
    public bool BlockedFromIssue { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public string ActorName { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }

    public const int MaxNarrativeLength = 500;
    public const int MaxPlaceLength = 200;

    private AccountableFormLossReport() { }

    public static AccountableFormLossReport Record(
        AccountableDocument document, Guid? custodianUserId, AccountableFormCopies copiesLost, DateOnly lostOn, string? place,
        string narrative, DateOnly reportedOn, bool blockedFromIssue, string actorId, string actorName, DateTime recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (copiesLost == 0 || (copiesLost & ~AccountableFormCopies.WholeSet) != 0)
            throw new ArgumentException("State what is missing: the whole set or one or more copies.", nameof(copiesLost));
        var story = (narrative ?? string.Empty).Trim();
        if (story.Length is 0 || story.Length > MaxNarrativeLength)
            throw new ArgumentException($"A narrative of up to {MaxNarrativeLength} characters is required.", nameof(narrative));
        var where = string.IsNullOrWhiteSpace(place) ? null : place.Trim();
        if (where?.Length > MaxPlaceLength)
            throw new ArgumentException("The place is too long.", nameof(place));
        if (lostOn > reportedOn)
            throw new ArgumentException("The loss cannot be dated after the day it was reported.", nameof(lostOn));
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(actorName))
            throw new ArgumentException("The reporting actor is required.");
        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The recording time must be UTC.", nameof(recordedAtUtc));
        return new AccountableFormLossReport
        {
            Id = Guid.NewGuid(), MunicipalityId = document.MunicipalityId, AccountableDocumentId = document.Id,
            CustodianUserId = custodianUserId, CopiesLost = copiesLost, LostOn = lostOn, Place = where,
            Narrative = story, ReportedOn = reportedOn, BlockedFromIssue = blockedFromIssue,
            ActorId = actorId.Trim(), ActorName = actorName.Trim(), RecordedAtUtc = recordedAtUtc
        };
    }
}
