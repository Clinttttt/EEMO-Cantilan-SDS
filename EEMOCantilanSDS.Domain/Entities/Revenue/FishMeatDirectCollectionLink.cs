using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// Office-confirmed evidence that a posted direct Fish/Meat Vendor Fee Collection, recorded against a payer name only, belongs to a registered vendor.
/// It is append-only and never edits the Collection, its SRC, its payer snapshot, its amount, remittance or any accountable document; the Collection
/// stays exactly as the Collector recorded it. A name match is never a link: only an explicit Head decision creates one, and a Collection has at most one.
/// </summary>
public sealed class FishMeatDirectCollectionLink : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid CollectionId { get; private set; }
    public Guid VendorRegistrationId { get; private set; }
    public Guid ClientOperationId { get; private set; }
    public string? Note { get; private set; }
    public Guid ActorId { get; private set; }
    public string ActorName { get; private set; } = "";
    public DateTime LinkedAtUtc { get; private set; }
    private FishMeatDirectCollectionLink() { }

    public static FishMeatDirectCollectionLink Confirm(Guid tenant, Guid collection, Guid vendor, Guid operation, string? note, Guid actorId, string actorName)
    {
        if (tenant == Guid.Empty || collection == Guid.Empty || vendor == Guid.Empty || operation == Guid.Empty || actorId == Guid.Empty)
            throw new ArgumentException("InvalidLinkIdentity");
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (text is { Length: > 300 }) throw new ArgumentException("LinkNoteTooLong");
        var actor = string.IsNullOrWhiteSpace(actorName) ? "Office User" : actorName.Trim();
        var now = DateTime.UtcNow;
        return new()
        {
            Id = Guid.NewGuid(), MunicipalityId = tenant, CollectionId = collection, VendorRegistrationId = vendor, ClientOperationId = operation,
            Note = text, ActorId = actorId, ActorName = actor[..Math.Min(100, actor.Length)], LinkedAtUtc = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc)
        };
    }
}
