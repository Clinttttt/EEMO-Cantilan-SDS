using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>Reviewed authority for cash fines without a registered account. Not an assessment policy or receivable.
/// No application setup writer exists: activation requires separately reviewed office evidence.</summary>
public sealed class DirectFineAuthority : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public Guid PenaltyVersionId { get; private set; }
    public string GoverningReference { get; private set; } = "";
    public string ApprovalReference { get; private set; } = "";
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool AllowsUncappedAmount { get; private set; }
    public bool RequiresPayerParticulars { get; private set; }
    public bool RequiresReason { get; private set; }
    public bool RequiresSupportingReference { get; private set; }
    public Guid VerifiedBy { get; private set; }
    public DateTime VerifiedAtUtc { get; private set; }
    private DirectFineAuthority() { }
    public static DirectFineAuthority Record(Guid tenant, Guid penaltyVersion, string governingReference,
        string approvalReference, DateOnly from, DateOnly? to, bool uncapped, bool payerRequired,
        bool reasonRequired, bool referenceRequired, Guid verifiedBy, DateTime verifiedAtUtc)
    {
        if (tenant == Guid.Empty || penaltyVersion == Guid.Empty || verifiedBy == Guid.Empty)
            throw new ArgumentException("Verified tenant, category and authority actor are required.");
        if (string.IsNullOrWhiteSpace(governingReference) || governingReference.Trim().Length > 200
            || string.IsNullOrWhiteSpace(approvalReference) || approvalReference.Trim().Length > 200)
            throw new ArgumentException("Governing and no-registration collection approval references are required.");
        if (to < from || verifiedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Invalid authority dates.");
        return new() { Id = Guid.NewGuid(), MunicipalityId = tenant, PenaltyVersionId = penaltyVersion,
            GoverningReference = governingReference.Trim(), ApprovalReference = approvalReference.Trim(),
            EffectiveFrom = from, EffectiveTo = to, AllowsUncappedAmount = uncapped,
            RequiresPayerParticulars = payerRequired, RequiresReason = reasonRequired,
            RequiresSupportingReference = referenceRequired, VerifiedBy = verifiedBy, VerifiedAtUtc = verifiedAtUtc };
    }
}
