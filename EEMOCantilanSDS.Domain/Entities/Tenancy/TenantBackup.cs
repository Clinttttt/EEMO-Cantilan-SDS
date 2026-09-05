using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Tenancy;

/// <summary>
/// A stored, restorable backup of ONE municipality's data — the per-LGU equivalent of the platform's
/// whole-database backups, but scoped to a single tenant. It holds a faithful, round-trippable snapshot
/// (the same <c>restore-v1</c> format the scoped restore accepts) as JSON text, plus lightweight metadata
/// for the "Recent backups" list. It is <see cref="IMunicipalityOwned"/>, so the global query filter
/// guarantees a Head only ever sees/restores their own municipality's backups. It is intentionally NOT
/// itself restorable (excluded from the restore whitelist), so a restore never wipes the backup history.
/// </summary>
public class TenantBackup : IMunicipalityOwned
{
    public Guid Id { get; private set; }

    /// <inheritdoc />
    public Guid MunicipalityId { get; private set; }

    /// <summary>When the backup was captured (UTC).</summary>
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>The Head/admin username who created the backup (or "system" for automatic ones).</summary>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <summary>Snapshot format version (e.g. <c>restore-v1</c>) — a restore refuses a mismatched version.</summary>
    public string FormatVersion { get; private set; } = string.Empty;

    /// <summary>Total rows captured across all tables (for the UI summary).</summary>
    public int RowCount { get; private set; }

    /// <summary>Number of tables captured (for the UI summary).</summary>
    public int TableCount { get; private set; }

    /// <summary>Size of the serialized snapshot in bytes (for the UI summary).</summary>
    public long SizeBytes { get; private set; }

    /// <summary>The serialized <c>TenantRestoreSnapshot</c> JSON — the exact bytes a restore reads.</summary>
    public string SnapshotJson { get; private set; } = string.Empty;

    /// <summary>Optional short label (e.g. "Manual", "Before restore").</summary>
    public string? Note { get; private set; }

    /// <summary>
    /// True where the platform took this backup on a schedule, false where somebody in the office asked for it.
    /// </summary>
    /// <remarks>
    /// A COLUMN rather than a reading of <see cref="CreatedBy"/>. The two classes are pruned against separate allowances, so which
    /// class a backup belongs to decides whether it survives - and deciding that by matching a display string would let a nightly
    /// run evict the deliberate backup an office took before doing something risky. <c>CreatedBy</c> already falls back to "system"
    /// when there is no signed-in user, so it could not tell the two apart in any case.
    /// </remarks>
    public bool IsAutomated { get; private set; }

    private TenantBackup() { } // EF Core

    public static TenantBackup Create(
        string createdBy,
        string formatVersion,
        int rowCount,
        int tableCount,
        long sizeBytes,
        string snapshotJson,
        string? note = null,
        bool isAutomated = false)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            throw new ArgumentException("Snapshot content is required.", nameof(snapshotJson));

        return new TenantBackup
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim(),
            FormatVersion = formatVersion,
            RowCount = rowCount,
            TableCount = tableCount,
            SizeBytes = sizeBytes,
            SnapshotJson = snapshotJson,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            IsAutomated = isAutomated,
        };
    }
}
