using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;

/// <summary>
/// Per-municipality stored backup history. Reads/writes are scoped to the caller's own tenant by the
/// global query filter (and the stamp interceptor). Creating a backup delegates the faithful snapshot
/// capture to <see cref="ITenantRestoreRepository"/> and stores it as JSON text with lightweight metadata;
/// restore-from-stored reuses the same atomic scoped restore. Retention keeps the last
/// <see cref="RetentionCount"/> backups per municipality so the table stays bounded.
/// </summary>
public class TenantBackupRepository(
    AppDbContext context,
    ITenantRestoreRepository restoreRepository,
    ICurrentUserService currentUser) : ITenantBackupRepository
{
    private const int RetentionCount = 15;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public async Task<TenantBackupInfo> CreateAsync(string? note, CancellationToken ct)
    {
        var snapshot = await restoreRepository.CreateSnapshotAsync(ct);
        var json = JsonSerializer.Serialize(snapshot, Options);

        var (rowCount, tableCount) = CountRows(snapshot);
        var sizeBytes = Encoding.UTF8.GetByteCount(json);

        var backup = TenantBackup.Create(
            createdBy: currentUser.Username ?? "system",
            formatVersion: snapshot.FormatVersion,
            rowCount: rowCount,
            tableCount: tableCount,
            sizeBytes: sizeBytes,
            snapshotJson: json,
            note: note);

        context.TenantBackups.Add(backup);          // MunicipalityId stamped by the interceptor
        await context.SaveChangesAsync(ct);

        await TrimHistoryAsync(ct);

        return ToInfo(backup);
    }

    public async Task<IReadOnlyList<TenantBackupInfo>> ListAsync(CancellationToken ct)
    {
        return await context.TenantBackups
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new TenantBackupInfo(
                b.Id, b.CreatedAtUtc, b.CreatedBy, b.RowCount, b.TableCount, b.SizeBytes, b.Note))
            .ToListAsync(ct);
    }

    public async Task<(TenantBackupInfo Info, byte[] Bytes)?> GetFileAsync(Guid id, CancellationToken ct)
    {
        var backup = await context.TenantBackups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (backup is null) return null;
        return (ToInfo(backup), Encoding.UTF8.GetBytes(backup.SnapshotJson));
    }

    public async Task<TenantRestoreSnapshot?> GetSnapshotAsync(Guid id, CancellationToken ct)
    {
        var backup = await context.TenantBackups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (backup is null) return null;
        try { return JsonSerializer.Deserialize<TenantRestoreSnapshot>(backup.SnapshotJson); }
        catch { return null; }
    }

    public async Task<TenantBackupContentsDto?> GetContentsAsync(Guid id, CancellationToken ct)
    {
        var backup = await context.TenantBackups.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        if (backup is null) return null;

        TenantRestoreSnapshot? snapshot;
        try { snapshot = JsonSerializer.Deserialize<TenantRestoreSnapshot>(backup.SnapshotJson); }
        catch { snapshot = null; }

        var tables = new List<TenantBackupTableDto>();
        if (snapshot is not null)
        {
            foreach (var kv in snapshot.Tables)
            {
                var count = CountArray(kv.Value);
                tables.Add(new TenantBackupTableDto(kv.Key, TenantBackupTableNames.Display(kv.Key), count));
            }
        }

        // Most meaningful first (rows desc), then alphabetical by friendly label.
        tables = tables
            .OrderByDescending(t => t.RowCount)
            .ThenBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TenantBackupContentsDto(
            backup.Id, backup.CreatedAtUtc, backup.CreatedBy, backup.RowCount, backup.TableCount,
            backup.SizeBytes, backup.Note, tables);
    }

    public async Task<IReadOnlyList<TenantRestoreEventDto>> ListRestoreEventsAsync(int take, CancellationToken ct)
    {
        return await context.AuditLogs
            .AsNoTracking()
            .Where(a => a.Action == "TenantRestore")
            .OrderByDescending(a => a.LoggedAt)
            .Take(take)
            .Select(a => new TenantRestoreEventDto(a.LoggedAt, a.ActorName, a.Notes ?? string.Empty))
            .ToListAsync(ct);
    }

    // Keep only the most recent RetentionCount backups for the caller's municipality (query-filter scoped).
    private async Task TrimHistoryAsync(CancellationToken ct)
    {
        var stale = await context.TenantBackups
            .OrderByDescending(b => b.CreatedAtUtc)
            .Skip(RetentionCount)
            .ToListAsync(ct);
        if (stale.Count == 0) return;
        context.TenantBackups.RemoveRange(stale);
        await context.SaveChangesAsync(ct);
    }

    private static TenantBackupInfo ToInfo(TenantBackup b) =>
        new(b.Id, b.CreatedAtUtc, b.CreatedBy, b.RowCount, b.TableCount, b.SizeBytes, b.Note);

    // Count rows across the snapshot's per-table JSON arrays (tableCount = tables that hold ≥1 row).
    private static (int Rows, int Tables) CountRows(TenantRestoreSnapshot snapshot)
    {
        var rows = 0;
        var tables = 0;
        foreach (var kv in snapshot.Tables)
        {
            var len = CountArray(kv.Value);
            rows += len;
            if (len > 0) tables++;
        }
        return (rows, tables);
    }

    // The element count of a JSON-array-text blob (0 for empty/malformed).
    private static int CountArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch { return 0; }
    }
}
