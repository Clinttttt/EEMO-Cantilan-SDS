using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// Durable tenant-scoped identity for one normalized posting intent and its terminal outcome.
/// </summary>
public sealed class PostingOperation : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid ClientOperationId { get; private set; }
    public int IntentVersion { get; private set; }
    public string NormalizedIntent { get; private set; } = string.Empty;
    public string IntentFingerprint { get; private set; } = string.Empty;
    public string Origin { get; private set; } = string.Empty;
    public string ActorId { get; private set; } = string.Empty;
    public PostingOperationStatus Status { get; private set; }
    public string? OutcomeCode { get; private set; }
    public string? OutcomeDetails { get; private set; }
    public Guid? CollectionId { get; private set; }
    public Guid? AccountableDocumentId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }

    private PostingOperation() { }

    public static PostingOperation Record(
        Guid municipalityId,
        Guid clientOperationId,
        int intentVersion,
        string normalizedIntent,
        string origin,
        string actorId,
        PostingOperationStatus status,
        string? outcomeCode,
        string? outcomeDetails,
        Guid? collectionId,
        Guid? accountableDocumentId,
        DateTime recordedAtUtc)
    {
        if (municipalityId == Guid.Empty || clientOperationId == Guid.Empty)
            throw new ArgumentException("Tenant and ClientOperationId are required.");
        if (intentVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(intentVersion));
        if (string.IsNullOrWhiteSpace(normalizedIntent) || normalizedIntent.Length > 65_536)
            throw new ArgumentException("Normalized posting intent is required and must not exceed 64 KB.", nameof(normalizedIntent));
        if (string.IsNullOrWhiteSpace(origin) || origin.Length > 40)
            throw new ArgumentException("Operation origin is required and must not exceed 40 characters.", nameof(origin));
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 100)
            throw new ArgumentException("Actor context is required and must not exceed 100 characters.", nameof(actorId));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (recordedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Operation outcome time must be UTC.", nameof(recordedAtUtc));
        if (collectionId == Guid.Empty || accountableDocumentId == Guid.Empty)
            throw new ArgumentException("Outcome identifiers must be valid when supplied.");
        if (status == PostingOperationStatus.Succeeded && !collectionId.HasValue)
            throw new ArgumentException("A successful posting outcome requires its Collection.", nameof(collectionId));
        if (status != PostingOperationStatus.Succeeded && string.IsNullOrWhiteSpace(outcomeCode))
            throw new ArgumentException("A terminal non-success outcome requires a stable outcome code.", nameof(outcomeCode));
        if (outcomeCode?.Length > 80 || outcomeDetails?.Length > 16_384)
            throw new ArgumentException("Outcome detail exceeds its limit.");

        // The application normalizes the business intent before it reaches this entity. Bind origin and
        // actor into the durable fingerprint too, so the same payload cannot be replayed under a changed
        // operation context while appearing to be the same attempted posting.
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(intentVersion);
            writer.Write(origin.Trim());
            writer.Write(actorId.Trim());
            writer.Write(normalizedIntent);
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        return new PostingOperation
        {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, ClientOperationId = clientOperationId,
            IntentVersion = intentVersion, NormalizedIntent = normalizedIntent,
            IntentFingerprint = fingerprint, Origin = origin.Trim(), ActorId = actorId.Trim(),
            Status = status, OutcomeCode = outcomeCode, OutcomeDetails = outcomeDetails,
            CollectionId = collectionId, AccountableDocumentId = accountableDocumentId,
            RecordedAtUtc = recordedAtUtc
        };
    }
}
