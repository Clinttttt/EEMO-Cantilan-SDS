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
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            try
            {
                using var doc = JsonDocument.Parse(kv.Value);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var len = doc.RootElement.GetArrayLength();
                    rows += len;
                    if (len > 0) tables++;
                }
            }
            catch { /* ignore a malformed table blob in the count */ }
        }
        return (rows, tables);
    }
}
