using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// An append-only external follow-up reference for one form: the RCD (or equivalent) that carried a cancelled form, or the
/// notice/report that evidences a loss. It is added AFTER the immediate cancellation or loss was recorded, so incomplete
/// external paperwork never delays blocking the serial and never lets it be reused. Without one the form shows "Needs
/// follow-up". It carries no money and changes no state.
/// </summary>
public sealed class AccountableFormReference : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid AccountableDocumentId { get; private set; }
    public AccountableFormReferenceKind Kind { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public string ActorName { get; private set; } = string.Empty;
    public DateTime RecordedAtUtc { get; private set; }

    public const int MaxReferenceLength = 200;
    public const int MaxNoteLength = 500;

    private AccountableFormReference() { }

    public static AccountableFormReference Record(
        AccountableDocument document, AccountableFormReferenceKind kind, string reference, string? note, string actorId,
        string actorName, DateTime recordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var text = (reference ?? string.Empty).Trim();
        if (text.Length is 0 or > MaxReferenceLength)
            throw new ArgumentException($"A reference of up to {MaxReferenceLength} characters is required.", nameof(reference));
        var extra = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (extra?.Length > MaxNoteLength) throw new ArgumentException("The note is too long.", nameof(note));
        if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(actorName))
            throw new ArgumentException("The recording actor is required.");
        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The recording time must be UTC.", nameof(recordedAtUtc));
        return new AccountableFormReference
        {
            Id = Guid.NewGuid(), MunicipalityId = document.MunicipalityId, AccountableDocumentId = document.Id,
            Kind = kind, Reference = text, Note = extra, ActorId = actorId.Trim(), ActorName = actorName.Trim(),
            RecordedAtUtc = recordedAtUtc
        };
    }
}
