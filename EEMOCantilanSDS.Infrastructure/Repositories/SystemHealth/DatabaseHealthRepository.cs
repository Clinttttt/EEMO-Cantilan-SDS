using System.Data;
using System.Data.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;

/// <summary>
/// Reads live PostgreSQL health metrics off the AppDbContext's own connection using read-only queries
/// against pg_* system views. Every metric is wrapped individually so a restricted role or an
/// unavailable view degrades that single value to a safe default (0 / null) instead of breaking the
/// whole snapshot. No writes are ever issued and no secrets are read or returned.
/// </summary>
public class DatabaseHealthRepository(
    AppDbContext context,
    EEMOCantilanSDS.Application.Common.Interface.Services.IComputeMetricsProvider computeMetrics,
    EEMOCantilanSDS.Application.Common.Interface.Persistence.ITenantUsageRepository tenantUsage) : IDatabaseHealthRepository
{
    // The per-municipality footprint is a multi-table estimate; cache it briefly so the 5s panel poll
    // never re-runs the heavy scan more than once a minute (keyed by tenant, so each LGU is independent).
    private static readonly object TenantSizeGate = new();
    private static readonly Dictionary<Guid, (long Bytes, DateTime At)> TenantSizeCache = new();
    private static readonly TimeSpan TenantSizeCacheFor = TimeSpan.FromSeconds(60);
    public async Task<DatabaseHealthDto> GetHealthAsync(CancellationToken ct)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = false;

        long sizeBytes = 0;
        var maxConnections = 0;
        int active = 0, idle = 0, total = 0;
        var blocked = 0;
        long deadlocks = 0;
        double cacheHitPct = 0;
        double? longestSeconds = null;
        DateTime? uptimeSince = null;
        double commitRatioPct = 0;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
                openedHere = true;
            }

            sizeBytes = await TryScalarAsync(connection,
                "SELECT pg_database_size(current_database());", 0L, Convert.ToInt64, ct);

            maxConnections = await TryScalarAsync(connection,
                "SELECT setting::int FROM pg_settings WHERE name='max_connections';", 0, Convert.ToInt32, ct);

            // Connections come back as a single row of three counts (active / idle-ish / total).
            (active, idle, total) = await TryConnectionCountsAsync(connection, ct);

            blocked = await TryScalarAsync(connection,
                "SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND cardinality(pg_blocking_pids(pid))>0;",
                0, Convert.ToInt32, ct);

            deadlocks = await TryScalarAsync(connection,
                "SELECT deadlocks FROM pg_stat_database WHERE datname=current_database();", 0L, Convert.ToInt64, ct);

            cacheHitPct = await TryScalarAsync(connection,
                "SELECT COALESCE(sum(blks_hit)::float/NULLIF(sum(blks_hit)+sum(blks_read),0),0)*100 FROM pg_stat_database WHERE datname=current_database();",
                0d, Convert.ToDouble, ct);

            commitRatioPct = await TryScalarAsync(connection,
                "SELECT COALESCE(sum(xact_commit)::float/NULLIF(sum(xact_commit)+sum(xact_rollback),0),0)*100 FROM pg_stat_database WHERE datname=current_database();",
                0d, Convert.ToDouble, ct);

            var longest = await TryScalarAsync(connection,
                "SELECT COALESCE(max(EXTRACT(EPOCH FROM (now()-query_start))),0) FROM pg_stat_activity WHERE datname=current_database() AND state='active' AND pid<>pg_backend_pid();",
                0d, Convert.ToDouble, ct);
            longestSeconds = longest;

            uptimeSince = await TryScalarNullableAsync(connection,
                "SELECT pg_postmaster_start_time();", Convert.ToDateTime, ct);
        }
        catch
        {
            // Even a failure to open the connection should still yield a well-formed snapshot of defaults.
        }
        finally
        {
            if (openedHere && connection.State == ConnectionState.Open)
            {
                try { await connection.CloseAsync(); } catch { /* best-effort */ }
            }
        }

        // Host compute metrics (CPU/memory/provisioned storage) from Azure Monitor — never breaks the
        // snapshot: the provider itself degrades to nulls on any failure.
        EEMOCantilanSDS.Application.Dtos.SystemHealth.ComputeMetrics compute;
        try { compute = await computeMetrics.GetAsync(ct); }
        catch { compute = new EEMOCantilanSDS.Application.Dtos.SystemHealth.ComputeMetrics(null, null, 0); }

        // This LGU's own data footprint (scoped to the caller's tenant), cached ~60s. Degrades to 0.
        long tenantBytes = 0;
        try
        {
            var mid = context.CurrentMunicipalityId;
            bool cached;
            lock (TenantSizeGate)
            {
                cached = TenantSizeCache.TryGetValue(mid, out var entry) && DateTime.UtcNow - entry.At < TenantSizeCacheFor;
                if (cached) tenantBytes = entry.Bytes;
            }
            if (!cached)
            {
                var usage = await tenantUsage.GetUsageAsync(ct);
                tenantBytes = usage.EstimatedSizeBytes;
                lock (TenantSizeGate) { TenantSizeCache[mid] = (tenantBytes, DateTime.UtcNow); }
            }
        }
        catch { /* degrade to 0 */ }

        return new DatabaseHealthDto(
            DatabaseSizeBytes: sizeBytes,
            ActiveConnections: active,
            IdleConnections: idle,
            TotalConnections: total,
            MaxConnections: maxConnections,
            BlockedQueries: blocked,
            Deadlocks: deadlocks,
            CacheHitRatioPct: cacheHitPct,
            LongestQuerySeconds: longestSeconds,
            UptimeSince: uptimeSince is { } u ? DateTime.SpecifyKind(u.ToUniversalTime(), DateTimeKind.Utc) : null,
            CollectedAt: DateTime.UtcNow,
            CommitRatioPct: commitRatioPct,
            CpuPercent: compute.CpuPercent,
            MemoryPercent: compute.MemoryPercent,
            ProvisionedStorageBytes: compute.ProvisionedStorageBytes,
            TenantSizeBytes: tenantBytes);    }

    private static async Task<T> TryScalarAsync<T>(
        DbConnection connection, string sql, T fallback, Func<object, T> convert, CancellationToken ct)
    {
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            var value = await cmd.ExecuteScalarAsync(ct);
            return value is null or DBNull ? fallback : convert(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static async Task<T?> TryScalarNullableAsync<T>(
        DbConnection connection, string sql, Func<object, T> convert, CancellationToken ct) where T : struct
    {
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            var value = await cmd.ExecuteScalarAsync(ct);
            return value is null or DBNull ? null : convert(value);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(int Active, int Idle, int Total)> TryConnectionCountsAsync(
        DbConnection connection, CancellationToken ct)
    {
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT count(*) FILTER (WHERE state='active'), " +
                "count(*) FILTER (WHERE state LIKE 'idle%'), " +
                "count(*) FROM pg_stat_activity WHERE datname=current_database();";

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var a = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                var i = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                var t = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
                return (a, i, t);
            }
        }
        catch
        {
            // fall through to defaults
        }

        return (0, 0, 0);
    }
}
