using EEMOCantilanSDS.Application.Command.Backup.TriggerBackup;
using EEMOCantilanSDS.Application.Command.Backup.TriggerRestore;
using EEMOCantilanSDS.Application.Queries.Backup.GetBackupRunDetail;
using EEMOCantilanSDS.Application.Queries.Backup.GetBackupRuns;
using EEMOCantilanSDS.Application.Queries.Backup.GetLatestBackupArtifact;
using EEMOCantilanSDS.Application.Requests.Backup;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

/// <summary>
/// Head-only (SuperAdmin) database backup operations. Every endpoint proxies to the server-side
/// <c>IBackupService</c>; the GitHub token never reaches the browser. No restore/upload is exposed.
/// </summary>
[Route("api/backup")]
[ApiController]
public class BackupController(ISender sender) : ApiBaseController(sender)
{
    /// <summary>Trigger the GitHub Actions backup workflow on demand.</summary>
    [HttpPost("run")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<bool>> RunAsync()
    {
        var result = await Sender.Send(new TriggerBackupCommand());
        return HandleResponse(result);
    }

    /// <summary>
    /// Trigger the destructive restore workflow. Head-only. The confirmation phrase and the admin
    /// password are both re-verified server-side in the handler before anything is dispatched.
    /// </summary>
    [HttpPost("restore")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<bool>> RestoreAsync([FromBody] RestoreRequest request)
    {
        var result = await Sender.Send(new TriggerRestoreCommand(request.ConfirmationPhrase, request.Password));
        return HandleResponse(result);
    }

    /// <summary>List recent backup workflow runs (newest first).</summary>
    [HttpGet("runs")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<IReadOnlyList<Application.Dtos.Backup.BackupRunDto>>> RunsAsync()
    {
        var result = await Sender.Send(new GetBackupRunsQuery());
        return HandleResponse(result);
    }

    /// <summary>Detailed step timeline for a single backup workflow run (the in-app "pipeline").</summary>
    [HttpGet("runs/{runId:long}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<Application.Dtos.Backup.BackupRunDetailDto>> DetailAsync(long runId)
    {
        var result = await Sender.Send(new GetBackupRunDetailQuery(runId));
        return HandleResponse(result);
    }

    /// <summary>Stream the latest successful backup artifact back to the Head for download.</summary>
    [HttpGet("latest")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> LatestAsync()
    {
        var r = await Sender.Send(new GetLatestBackupArtifactQuery());
        if (!r.IsSuccess || r.Value is null)
            return StatusCode(r.StatusCode ?? 500, new { error = r.Error });

        return File(r.Value.Content, r.Value.ContentType, r.Value.FileName);
    }
}
