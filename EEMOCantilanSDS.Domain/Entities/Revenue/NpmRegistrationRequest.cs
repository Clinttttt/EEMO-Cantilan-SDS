using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

public enum NpmRegistrationRequestStatus { Submitted = 1, Approved = 2, Returned = 3, Rejected = 4, Cancelled = 5 }

/// <summary>Nonfinancial office review. No amount, cash custody, receipt or Collection identity belongs here.</summary>
public sealed class NpmRegistrationRequest : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid CollectorId { get; private set; }
    public string CollectorName { get; private set; } = "";
    public Guid VendorRegistrationId { get; private set; }
    public int TaxYear { get; private set; }
    public string VendorName { get; private set; } = "";
    public Guid ClientOperationId { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public Guid? ReplacesRequestId { get; private set; }
    public string ProposedStall { get; private set; } = "";
    public MarketSection? ProposedSection { get; private set; }
    public string Note { get; private set; } = "";
    public DateTime SubmittedAtUtc { get; private set; }
    public NpmRegistrationRequestStatus Status { get; private set; }
    public int Version { get; private set; }
    public Guid? DecisionOperationId { get; private set; }
    public Guid? AssociationId { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public string? ReviewReason { get; private set; }
    private NpmRegistrationRequest() { }
    public static NpmRegistrationRequest Submit(Guid tenant, Guid collector, string collectorName, Guid vendor, int year,
        string vendorName, Guid operation, string fingerprint, string stall, MarketSection? section, string note, DateTime now, Guid? replaces = null)
    {
        if (new[] { tenant, collector, vendor, operation }.Any(x => x == Guid.Empty) || year is < 2000 or > 2200
            || section.HasValue && !Enum.IsDefined(section.Value)) throw new ArgumentException("Invalid registration request identity.");
        return new() { MunicipalityId = tenant, CollectorId = collector, CollectorName = Text(collectorName, 100), VendorRegistrationId = vendor,
            TaxYear = year, VendorName = Text(vendorName, 200), ClientOperationId = operation, Fingerprint = fingerprint,
            ProposedStall = Text(stall, 20), ProposedSection = section, Note = Text(note, 1000), SubmittedAtUtc = now,
            ReplacesRequestId = replaces, Status = NpmRegistrationRequestStatus.Submitted, Version = 1 };
    }
    public void Review(Guid operation, int expectedVersion, NpmRegistrationRequestStatus outcome, string actor, string reason, DateTime now, Guid? association)
    {
        if (operation == Guid.Empty || expectedVersion != Version || Status != NpmRegistrationRequestStatus.Submitted)
            throw new InvalidOperationException("Request changed; refresh before reviewing.");
        if (outcome is not (NpmRegistrationRequestStatus.Approved or NpmRegistrationRequestStatus.Returned or NpmRegistrationRequestStatus.Rejected)
            || (outcome == NpmRegistrationRequestStatus.Approved) != association.HasValue)
            throw new ArgumentException("Approval requires a verified NPM association; return/rejection must not create one.");
        ReviewReason = Text(reason, 1000); ReviewedBy = Text(actor, 100); DecisionOperationId = operation;
        ReviewedAtUtc = now; AssociationId = association; Status = outcome; Version++;
    }
    public void Cancel(Guid operation, int expectedVersion, string actor, DateTime now)
    {
        if (operation == Guid.Empty || expectedVersion != Version || Status != NpmRegistrationRequestStatus.Submitted)
            throw new InvalidOperationException("Request changed; refresh before cancelling.");
        ReviewedBy = Text(actor, 100);
        ReviewReason = "Cancelled by submitting collector";
        ReviewedAtUtc = now;
        DecisionOperationId = operation;
        AssociationId = null;
        Status = NpmRegistrationRequestStatus.Cancelled;
        Version++;
    }
    private static string Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
        ? value.Trim() : throw new ArgumentException("Complete the required request evidence within the supported length.");
}
