using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Application.Queries.SystemHealth.GetTenantUsage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Per-tenant storage footprint for the authenticated Head's OWN municipality. Unlike the whole-database
/// <see cref="DatabaseHealthController"/> (platform-operator only), this is available to every LGU's Head
/// and is scoped server-side to the caller's tenant — it can only ever report the caller's own data.
/// Read-only: it issues COUNT / pg_column_size aggregates filtered by the caller's MunicipalityId.
/// </summary>
[Route("api/tenant-usage")]
[ApiController]
public class TenantUsageController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Record counts + estimated storage for the caller's municipality.</summary>
    [HttpGet]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<TenantUsageDto>> GetAsync()
    {
        var result = await Sender.Send(new GetTenantUsageQuery());
        return HandleResponse(result);
    }

    /// <summary>
    /// Downloads a JSON export of the caller's OWN municipality data (operational/financial + audit;
    /// credentials excluded). Scoped server-side to the caller's tenant — never another LGU, never the
    /// whole database (that stays on the platform-operator-only backup).
    /// </summary>
    [HttpGet("export")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> ExportAsync()
    {
        var result = await Sender.Send(new EEMOCantilanSDS.Application.Queries.Backup.ExportTenantData.ExportTenantDataQuery());
        if (result.IsSuccess && result.Value is not null)
            return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);

        var status = result.StatusCode is 200 or 0 or null ? 500 : result.StatusCode.Value;
        return StatusCode(status, new { IsSuccess = false, Error = result.Error });
    }

    /// <summary>
    /// Downloads a round-trippable snapshot of the caller's OWN municipality — the ONLY file the scoped
    /// restore accepts. Scoped server-side to the caller's tenant.
    /// </summary>
    [HttpGet("restore-snapshot")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> RestoreSnapshotAsync()
    {
        var result = await Sender.Send(new EEMOCantilanSDS.Application.Queries.Backup.GetTenantRestoreSnapshot.GetTenantRestoreSnapshotQuery());
        if (result.IsSuccess && result.Value is not null)
            return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);

        var status = result.StatusCode is 200 or 0 or null ? 500 : result.StatusCode.Value;
        return StatusCode(status, new { IsSuccess = false, Error = result.Error });
    }

    /// <summary>
    /// Restores the caller's OWN municipality from an uploaded snapshot. Confirmation phrase + password are
    /// re-verified server-side; the restore is a single scoped transaction (any failure = zero changes) and
    /// rejects a snapshot belonging to a different municipality.
    /// </summary>
    [HttpPost("restore")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<Application.Dtos.Backup.TenantRestoreResult>> RestoreAsync(
        [FromBody] Application.Requests.Backup.TenantRestoreRequest request)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.SnapshotBase64 ?? string.Empty); }
        catch { return BadRequest(new { IsSuccess = false, Error = "The backup file could not be read." }); }

        var result = await Sender.Send(new Application.Command.Backup.RestoreTenantData.RestoreTenantDataCommand(
            bytes, request.ConfirmationPhrase, request.Password));
        return HandleResponse(result);
    }

    // ── Stored backup history (per-municipality; the scoped equivalent of the whole-DB console) ──

    /// <summary>Create + store a new backup of the caller's OWN municipality (retention keeps the last N).</summary>
    [HttpPost("backups")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<Application.Dtos.Backup.TenantBackupInfo>> CreateBackupAsync(
        [FromBody] Application.Command.Backup.CreateTenantBackup.CreateTenantBackupCommand? command)
    {
        var result = await Sender.Send(command ?? new Application.Command.Backup.CreateTenantBackup.CreateTenantBackupCommand());
        return HandleResponse(result);
    }

    /// <summary>The caller's OWN stored backups (metadata only), newest first.</summary>
    [HttpGet("backups")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<IReadOnlyList<Application.Dtos.Backup.TenantBackupInfo>>> ListBackupsAsync()
    {
        var result = await Sender.Send(new Application.Queries.Backup.GetTenantBackups.GetTenantBackupsQuery());
        return HandleResponse(result);
    }

    /// <summary>Download one of the caller's OWN stored backups as its restore-ready JSON file.</summary>
    [HttpGet("backups/{id:guid}/file")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> DownloadBackupAsync(Guid id)
    {
        var result = await Sender.Send(new Application.Queries.Backup.GetTenantBackupFile.GetTenantBackupFileQuery(id));
        if (result.IsSuccess && result.Value is not null)
            return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);

        var status = result.StatusCode is 200 or 0 or null ? 404 : result.StatusCode.Value;
        return StatusCode(status, new { IsSuccess = false, Error = result.Error });
    }

    /// <summary>
    /// Restore the caller's OWN municipality from a STORED backup. Confirmation phrase + password are
    /// re-verified server-side; the restore is a single scoped transaction (any failure = zero changes).
    /// </summary>
    [HttpPost("backups/{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<Application.Dtos.Backup.TenantRestoreResult>> RestoreFromBackupAsync(
        Guid id, [FromBody] Application.Requests.Backup.BackupRestoreRequest request)
    {
        var result = await Sender.Send(new Application.Command.Backup.RestoreTenantFromBackup.RestoreTenantFromBackupCommand(
            id, request.ConfirmationPhrase, request.Password));
        return HandleResponse(result);
    }

    /// <summary>Recent restore events for the caller's OWN municipality (from the audit log).</summary>
    [HttpGet("restore-history")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<IReadOnlyList<Application.Dtos.Backup.TenantRestoreEventDto>>> RestoreHistoryAsync()
    {
        var result = await Sender.Send(new Application.Queries.Backup.GetTenantRestoreHistory.GetTenantRestoreHistoryQuery());
        return HandleResponse(result);
    }
}
