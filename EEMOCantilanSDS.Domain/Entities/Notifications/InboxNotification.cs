using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Notifications;

/// <summary>Who a message is for. Staff are the office's Head and Admins; a Payor is a signed-in portal account.</summary>
public enum InboxRecipientKind
{
    Staff = 1,
    Payor = 2,
}

/// <summary>
/// One message to one person about one fact. <see cref="DedupeKey"/> names the fact, so the unique index keeps a repeated pass from
/// telling anyone twice. A message only reports what the owning records already say: it is never evidence of a payment or a charge.
/// </summary>
public sealed class InboxNotification : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public InboxRecipientKind RecipientKind { get; private set; }
    public Guid RecipientId { get; private set; }
    public string DedupeKey { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    public string? Link { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }

    private InboxNotification() { }

    public static InboxNotification Create(Guid municipalityId, InboxRecipientKind kind, Guid recipientId, string dedupeKey, string title, string body, string? link, DateTime now)
    {
        if (municipalityId == Guid.Empty || recipientId == Guid.Empty) throw new ArgumentException("A message needs an office and a recipient.");
        if (string.IsNullOrWhiteSpace(dedupeKey) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("A message needs a key, a title and a body.");
        return new InboxNotification
        {
            MunicipalityId = municipalityId,
            RecipientKind = kind,
            RecipientId = recipientId,
            DedupeKey = dedupeKey,
            Title = title,
            Body = body,
            Link = link,
            CreatedAt = now,
        };
    }

    public void MarkRead(DateTime now) => ReadAt ??= now;
}
