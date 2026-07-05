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
    DateTime CollectedAt);
