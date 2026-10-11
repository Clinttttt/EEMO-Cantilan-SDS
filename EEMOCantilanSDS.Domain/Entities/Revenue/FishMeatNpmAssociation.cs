using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>Office-confirmed registration evidence only; never a financial source or occupancy transfer.</summary>
public sealed class FishMeatNpmAssociation : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid VendorRegistrationId { get; private set; }
    public Guid ContractId { get; private set; }
    public Guid StallId { get; private set; }
    public Guid ClientOperationId { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public string OccupantSnapshot { get; private set; } = "";
    public string SourceVersion { get; private set; } = "";
    public string ConfirmationNote { get; private set; } = "";
    public string ConfirmedBy { get; private set; } = "";
    public DateTime ConfirmedAtUtc { get; private set; }
    public Guid? CorrectionOperationId { get; private set; }
    public string? CorrectionReason { get; private set; }
    public string? CorrectedBy { get; private set; }
    public DateTime? CorrectedAtUtc { get; private set; }
    private FishMeatNpmAssociation() { }

    public static FishMeatNpmAssociation Confirm(Guid tenant, Guid vendor, Guid contract, Guid stall,
        Guid operation, string fingerprint, string occupant, string version, string note, string actor)
    {
        if (tenant == Guid.Empty || vendor == Guid.Empty || contract == Guid.Empty || stall == Guid.Empty || operation == Guid.Empty)
            throw new ArgumentException("InvalidAssociationIdentity");
        return new() { Id = Guid.NewGuid(), MunicipalityId = tenant, VendorRegistrationId = vendor,
            ContractId = contract, StallId = stall, ClientOperationId = operation, RequestFingerprint = fingerprint,
            OccupantSnapshot = Text(occupant, 100), SourceVersion = version, ConfirmationNote = Text(note, 500),
            ConfirmedBy = Text(actor, 100), ConfirmedAtUtc = Timestamp() };
    }
    public void Correct(Guid operation, string reason, string actor)
    {
        if (operation == Guid.Empty || CorrectedAtUtc.HasValue) throw new ArgumentException("AssociationAlreadyCorrected");
        CorrectionReason = Text(reason, 500); CorrectedBy = Text(actor, 100);
        CorrectionOperationId = operation; CorrectedAtUtc = Timestamp();
    }
    private static DateTime Timestamp() { var now = DateTime.UtcNow; return new(now.Ticks - now.Ticks % 10, DateTimeKind.Utc); }
    private static string Text(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
        ? value.Trim() : throw new ArgumentException("InvalidAssociationEvidence");
}
