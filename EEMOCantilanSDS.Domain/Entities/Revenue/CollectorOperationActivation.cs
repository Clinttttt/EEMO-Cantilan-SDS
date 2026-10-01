using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// The one-time, prospective activation of an operation for canonical Collector Mobile collection in a tenant (WCF, 2026-10-01).
/// From <see cref="EffectiveFrom"/> new activity of the operation is canonical from birth — no per-payor or per-period cutover.
/// It is a boundary, not a migration: it rewrites no historical source, and a legacy source keeps its own explicit cutover.
/// One row per tenant and operation; activation is never repeated or moved.
/// </summary>
public sealed class CollectorOperationActivation : BaseEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public string OperationCode { get; private set; } = string.Empty;
    public DateOnly EffectiveFrom { get; private set; }
    public DateTime ActivatedAtUtc { get; private set; }
    public Guid ActivatedByUserId { get; private set; }
    public string ActivatedBy { get; private set; } = string.Empty;

    private CollectorOperationActivation() { }

    public static CollectorOperationActivation Activate(
        Guid municipalityId, string operationCode, DateOnly effectiveFrom, Guid activatedByUserId, string activatedBy,
        DateTime activatedAtUtc)
    {
        if (municipalityId == Guid.Empty)
            throw new ArgumentException("A tenant is required.", nameof(municipalityId));
        if (operationCode != CollectorOperationCodes.Wcf)
            throw new ArgumentException("Only WCF has an operation-level Mobile activation.", nameof(operationCode));
        if (activatedByUserId == Guid.Empty || string.IsNullOrWhiteSpace(activatedBy) || activatedBy.Trim().Length > 100)
            throw new ArgumentException("The activating actor is required.", nameof(activatedBy));
        if (activatedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The activation time must be UTC.", nameof(activatedAtUtc));

        return new CollectorOperationActivation
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            OperationCode = operationCode,
            EffectiveFrom = effectiveFrom,
            ActivatedAtUtc = activatedAtUtc,
            ActivatedByUserId = activatedByUserId,
            ActivatedBy = activatedBy.Trim()
        };
    }
}
