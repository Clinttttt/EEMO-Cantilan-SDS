using System;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Domain.Entities.Tenancy;

/// <summary>
/// One municipality's choice about one revenue operation: whether it is listed in the Head Portal's Operations directory and
/// what the municipality calls it. One row per tenant and operation code.
/// </summary>
/// <remarks>
/// <para>
/// This is configuration of PRESENTATION only. Being listed is not financial authorization: whether money may be received for
/// an operation is decided by its own classification policy, source registration, Collector assignment and role, and a
/// missing or unlisted row never switches any of that on or off. Hiding an operation deletes nothing and rewrites no history.
/// </para>
/// <para>
/// A municipality with NO rows has not adopted tenant configuration (every municipality activated before it existed), and is
/// read with the documented legacy fallback — every operation listed under its established name — rather than as "nothing
/// enabled". Rows exist for every catalog code once a municipality has adopted, so an explicit "not selected" is a row with
/// <see cref="IsListed"/> false, distinct from "never asked".
/// </para>
/// </remarks>
public class TenantOperationSetting : AuditableEntity, IMunicipalityOwned
{
    /// <inheritdoc />
    public Guid MunicipalityId { get; private set; }

    /// <summary>A stable <see cref="TenantOperationCatalog"/> code. Never derived from a label.</summary>
    public string OperationCode { get; private set; } = string.Empty;

    /// <summary>Whether the operation appears in the tenant's ordinary Operations directory.</summary>
    public bool IsListed { get; private set; }

    /// <summary>The municipality's own name for the operation, or null to use the neutral standard name.</summary>
    public string? LocalDisplayName { get; private set; }

    /// <summary>Where the row came from: "Onboarding" (declared at onboarding), "Head" (edited in the portal) or "Legacy" (recorded when an existing municipality adopted configuration).</summary>
    public string Origin { get; private set; } = "Head";

    /// <summary>Optimistic-concurrency token: two Heads editing one row cannot silently overwrite each other.</summary>
    public long Version { get; private set; } = 1;

    private TenantOperationSetting() { }

    public static TenantOperationSetting Create(
        string operationCode, bool isListed, string? localDisplayName, string origin, Guid municipalityId, string createdBy)
    {
        if (!TenantOperationCatalog.IsKnown(operationCode))
            throw new ArgumentException($"“{operationCode}” is not a revenue operation StallTrack recognises.", nameof(operationCode));
        if (municipalityId == Guid.Empty)
            throw new ArgumentException("A tenant operation setting belongs to a municipality.", nameof(municipalityId));

        return new TenantOperationSetting
        {
            Id = Guid.NewGuid(),
            MunicipalityId = municipalityId,
            OperationCode = operationCode,
            IsListed = isListed,
            LocalDisplayName = TenantOperationCatalog.NormalizeLabel(localDisplayName),
            Origin = origin,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };
    }

    /// <summary>Applies an edit. Returns false (and changes nothing, version included) when it would change nothing.</summary>
    public bool Update(bool isListed, string? localDisplayName, string updatedBy)
    {
        var label = TenantOperationCatalog.NormalizeLabel(localDisplayName);
        if (IsListed == isListed && string.Equals(LocalDisplayName, label, StringComparison.Ordinal)) return false;

        IsListed = isListed;
        LocalDisplayName = label;
        Origin = "Head";
        Version++;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
        return true;
    }
}
