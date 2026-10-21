using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

public enum FishMeatRequestStatus { Submitted = 1, Approved = 2, Returned = 3, Rejected = 4, Cancelled = 5 }

/// <summary>Proposed identity only. Decisions never constitute a collection or NPM tenancy.</summary>
public sealed class FishMeatRegistrationRequest : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid CollectorId { get; private set; }
    public string SubmittedBy { get; private set; } = "";
    public Guid ClientOperationId { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string PendingKey { get; private set; } = "";
    public string VendorName { get; private set; } = "";
    public FishMeatVendorType VendorType { get; private set; }
    public int TaxYear { get; private set; }
    public string? Contact { get; private set; }
    public string? Note { get; private set; }
    public DateTime SubmittedAtUtc { get; private set; }
    public Guid? ReplacesRequestId { get; private set; }
    public FishMeatRequestStatus Status { get; private set; }
    public int Version { get; private set; }
    public Guid? DecisionOperationId { get; private set; }
    public string? DecisionFingerprint { get; private set; }
    public Guid? RegistrationId { get; private set; }
    public string? ReviewedBy { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? Reason { get; private set; }

    private FishMeatRegistrationRequest() { }
    public static FishMeatRegistrationRequest Submit(Guid tenant, Guid collector, string actor, Guid operation,
        string fingerprint, string pendingKey, string name, FishMeatVendorType type, int year,
        string? contact, string? note, DateTime now, Guid? replaces)
    {
        if (new[] { tenant, collector, operation }.Any(x => x == Guid.Empty) || !Enum.IsDefined(type)
            || year is < 2000 or > 2200 || replaces == Guid.Empty || now.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Invalid registration request identity.");
        return new() { MunicipalityId = tenant, CollectorId = collector, SubmittedBy = Required(actor, 100),
            ClientOperationId = operation, Fingerprint = Required(fingerprint, 64), PendingKey = Required(pendingKey, 64),
            VendorName = Required(name, 200), VendorType = type, TaxYear = year, Contact = Text(contact, 200),
            Note = Text(note, 1000), SubmittedAtUtc = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc), ReplacesRequestId = replaces,
            Status = FishMeatRequestStatus.Submitted, Version = 1 };
    }

    public void Decide(Guid operation, string fingerprint, int version, FishMeatRequestStatus outcome,
        string actor, string reason, DateTime now, Guid? registration, Guid actorId)
    {
        if (operation == Guid.Empty || Version != version || Status != FishMeatRequestStatus.Submitted)
            throw new InvalidOperationException("Request changed; refresh before deciding.");
        if (outcome is not (FishMeatRequestStatus.Approved or FishMeatRequestStatus.Returned or FishMeatRequestStatus.Rejected or FishMeatRequestStatus.Cancelled)
            || (outcome == FishMeatRequestStatus.Approved) != registration.HasValue || registration == Guid.Empty
            || now.Kind != DateTimeKind.Utc || actorId == Guid.Empty)
            throw new ArgumentException("Approval requires a verified registration; other decisions cannot link one.");
        Reason = Required(reason, 1000); ReviewedBy = Required(actor, 100);
        ReviewedByUserId = actorId;
        DecisionFingerprint = Required(fingerprint, 64); DecisionOperationId = operation;
        RegistrationId = registration; ReviewedAtUtc = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc); Status = outcome; Version++;
    }

    private static string Required(string? value, int max) => Text(value, max) ?? throw new ArgumentException("Required request evidence is missing.");
    private static string? Text(string? value, int max) => value?.Trim() is not { Length: > 0 } text ? null
        : text.Length <= max ? text : throw new ArgumentException("Request evidence exceeds the supported length.");
}
