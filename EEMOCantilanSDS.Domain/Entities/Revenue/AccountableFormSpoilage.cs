using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// The auditable record that a BLANK accountable form was spoiled or cancelled before any financial use (IA-052): serial,
/// reason, actor and time, and the collector who held it when it was. It consumes inventory and represents no money: it is
/// never a Collection, never revenue and never returns to stock. An issued or consumed document is not spoiled here; it
/// keeps its linked correction history.
/// </summary>
public sealed class AccountableFormSpoilage : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid AccountableDocumentId { get; private set; }
    public Guid? CustodianUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public string ActorName { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }

    private AccountableFormSpoilage() { }

    public static AccountableFormSpoilage Record(
        AccountableDocument document, Guid? custodianUserId, string reason, string? note, string actorId,
        string actorName, DateTime recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        var why = (reason ?? string.Empty).Trim();
        if (why.Length is 0 or > 200)
            throw new ArgumentException("A reason of up to 200 characters is required.", nameof(reason));
        var extra = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (extra?.Length > 500)
            throw new ArgumentException("The note is too long.", nameof(note));
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(actorName))
            throw new ArgumentException("The recording actor is required.");
        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The recording time must be UTC.", nameof(recordedAtUtc));
        return new AccountableFormSpoilage
        {
            Id = Guid.NewGuid(),
            MunicipalityId = document.MunicipalityId,
            AccountableDocumentId = document.Id,
            CustodianUserId = custodianUserId,
            Reason = why,
            Note = extra,
            ActorId = actorId.Trim(),
            ActorName = actorName.Trim(),
            RecordedAtUtc = recordedAtUtc
        };
    }
}
