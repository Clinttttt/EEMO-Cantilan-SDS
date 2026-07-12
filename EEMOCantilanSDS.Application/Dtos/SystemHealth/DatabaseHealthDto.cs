namespace EEMOCantilanSDS.Application.Dtos.SystemHealth;

/// <summary>
/// A point-in-time snapshot of PostgreSQL server health, read strictly from pg_* system views.
/// Contains no secrets and no host-level (CPU/memory) data — only what the database can report about
/// itself. Surfaced on the Head/Admin-only Settings page and refreshed on a short interval.
/// </summary>
public record DatabaseHealthDto(
    long DatabaseSizeBytes,
    int ActiveConnections,
    int IdleConnections,
    int TotalConnections,
    int MaxConnections,
    int BlockedQueries,
    long Deadlocks,
    double CacheHitRatioPct,
    double? LongestQuerySeconds,
    DateTime? UptimeSince,
    DateTime CollectedAt,
    // Transaction success rate: committed / (committed + rolled back). A healthy DB sits near 100%;
    // a dip signals frequent rollbacks/aborted work. Read from pg_stat_database (degrades to 0).
    double CommitRatioPct = 0,
    // Host compute metrics from Azure Monitor (PostgreSQL cannot report these). Null when unavailable
    // (no permission / not configured / transient) → the UI shows "—". Storage is the provisioned size.
    double? CpuPercent = null,
    double? MemoryPercent = null,
    long ProvisionedStorageBytes = 0,
    // The CALLER'S municipality data footprint (scoped, from the tenant-usage estimate) — shown as
    // "used / provisioned" so each LGU sees its own storage, never the whole shared database.
    long TenantSizeBytes = 0);
