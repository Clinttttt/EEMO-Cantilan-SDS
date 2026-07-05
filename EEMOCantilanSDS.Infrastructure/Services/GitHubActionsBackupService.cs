using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Infrastructure.Services;

/// <summary>
/// <see cref="IBackupService"/> implementation over the GitHub REST API. The HttpClient is configured
/// in <c>AddInfrastructureService</c> with the base address, standard GitHub headers, and the Bearer
/// token — so the token never leaves the server and is never handled here directly.
/// All GitHub responses are translated into the app's <see cref="Result{T}"/>; failures return a
/// friendly message and never surface the token or raw provider internals.
/// </summary>
public class GitHubActionsBackupService(HttpClient http, GitHubBackupOptions options) : IBackupService
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result<bool>> TriggerBackupAsync(CancellationToken ct)
    {
        try
        {
            var url = $"repos/{options.Owner}/{options.Repo}/actions/workflows/{options.WorkflowFileName}/dispatches";
            using var response = await http.PostAsJsonAsync(url, new { @ref = options.Ref }, ct);

            if (response.StatusCode == HttpStatusCode.NoContent)
                return Result<bool>.Success(true);

            return Result<bool>.Failure(FriendlyMessage(response.StatusCode, "start the backup"), (int)response.StatusCode);
        }
        catch (Exception)
        {
            return Result<bool>.Failure("Could not reach the backup service. Please try again.", 502);
        }
    }

    public async Task<Result<IReadOnlyList<BackupRunDto>>> GetRecentRunsAsync(int count, CancellationToken ct)
    {
        try
        {
            var runs = await FetchRunsAsync(count, ct);
            if (runs is null)
                return Result<IReadOnlyList<BackupRunDto>>.Failure("Could not load recent backups.", 502);

            return Result<IReadOnlyList<BackupRunDto>>.Success(runs);
        }
        catch (Exception)
        {
            return Result<IReadOnlyList<BackupRunDto>>.Failure("Could not reach the backup service. Please try again.", 502);
        }
    }

    public async Task<Result<BackupRunDetailDto>> GetRunDetailAsync(long runId, CancellationToken ct)
    {
        try
        {
            // 1) Run object → run-level summary (status/conclusion/event/times/html_url).
            using var runResp = await http.GetAsync($"repos/{options.Owner}/{options.Repo}/actions/runs/{runId}", ct);
            if (!runResp.IsSuccessStatusCode)
                return Result<BackupRunDetailDto>.Failure(FriendlyMessage(runResp.StatusCode, "load this backup run"), (int)runResp.StatusCode);

            using var runDoc = JsonDocument.Parse(await runResp.Content.ReadAsStringAsync(ct));
            var run = runDoc.RootElement;

            var status = ReadString(run, "status") ?? "unknown";
            var conclusion = ReadString(run, "conclusion");
            var evt = ReadString(run, "event");
            var htmlUrl = ReadString(run, "html_url") ?? string.Empty;
            var startedAt = ReadDate(run, "run_started_at") ?? ReadDate(run, "created_at");
            var completedAt = ReadDate(run, "updated_at");

            // 2) Jobs → flatten each job's steps (our backup workflow has a single job).
            using var jobsResp = await http.GetAsync($"repos/{options.Owner}/{options.Repo}/actions/runs/{runId}/jobs", ct);
            if (!jobsResp.IsSuccessStatusCode)
                return Result<BackupRunDetailDto>.Failure(FriendlyMessage(jobsResp.StatusCode, "load this backup run"), (int)jobsResp.StatusCode);

            using var jobsDoc = JsonDocument.Parse(await jobsResp.Content.ReadAsStringAsync(ct));
            var steps = new List<BackupStepDto>();
            if (jobsDoc.RootElement.TryGetProperty("jobs", out var jobs) && jobs.ValueKind == JsonValueKind.Array)
            {
                foreach (var job in jobs.EnumerateArray())
                {
                    if (!job.TryGetProperty("steps", out var jobSteps) || jobSteps.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var step in jobSteps.EnumerateArray())
                    {
                        var number = step.TryGetProperty("number", out var numEl) && numEl.TryGetInt32(out var numVal)
                            ? numVal : steps.Count + 1;
                        var name = ReadString(step, "name") ?? "Step";
                        var stStatus = ReadString(step, "status") ?? "unknown";
                        var stConclusion = ReadString(step, "conclusion");
                        var stStarted = ReadDate(step, "started_at");
                        var stCompleted = ReadDate(step, "completed_at");

                        steps.Add(new BackupStepDto(number, name, stStatus, stConclusion, stStarted, stCompleted));
                    }
                }
            }

            var detail = new BackupRunDetailDto(runId, status, conclusion, startedAt, completedAt, evt, htmlUrl, steps);
            return Result<BackupRunDetailDto>.Success(detail);
        }
        catch (Exception)
        {
            return Result<BackupRunDetailDto>.Failure("Could not reach the backup service. Please try again.", 502);
        }
    }

    public async Task<Result<BackupArtifact>> GetLatestArtifactAsync(CancellationToken ct)    {
        try
        {
            // Look a little deeper than the display count so a run of failures doesn't hide the last good backup.
            var runs = await FetchRunsRawAsync(30, ct);
            if (runs is null)
                return Result<BackupArtifact>.Failure("Could not load recent backups.", 502);

            // Newest successful run first (the runs endpoint already returns newest-first).
            long? successRunId = null;
            foreach (var run in runs.Value.EnumerateArray())
            {
                if (run.TryGetProperty("conclusion", out var concl)
                    && concl.ValueKind == JsonValueKind.String
                    && string.Equals(concl.GetString(), "success", StringComparison.OrdinalIgnoreCase)
                    && run.TryGetProperty("id", out var idEl))
                {
                    successRunId = idEl.GetInt64();
                    break;
                }
            }

            if (successRunId is null)
                return Result<BackupArtifact>.Failure("No backup artifact available yet.", 404);

            // Artifacts for that run.
            using var artifactsResp = await http.GetAsync(
                $"repos/{options.Owner}/{options.Repo}/actions/runs/{successRunId}/artifacts", ct);
            if (!artifactsResp.IsSuccessStatusCode)
                return Result<BackupArtifact>.Failure(FriendlyMessage(artifactsResp.StatusCode, "load the backup artifact"), (int)artifactsResp.StatusCode);

            using var artifactsDoc = JsonDocument.Parse(await artifactsResp.Content.ReadAsStringAsync(ct));
            if (!artifactsDoc.RootElement.TryGetProperty("artifacts", out var artifacts)
                || artifacts.ValueKind != JsonValueKind.Array
                || artifacts.GetArrayLength() == 0)
            {
                return Result<BackupArtifact>.Failure("No backup artifact available yet.", 404);
            }

            var first = artifacts[0];
            if (!first.TryGetProperty("id", out var artifactIdEl))
                return Result<BackupArtifact>.Failure("No backup artifact available yet.", 404);

            var artifactId = artifactIdEl.GetInt64();
            var name = first.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString() ?? "stalltrack-backup"
                : "stalltrack-backup";

            // Download the zip. GitHub answers with a 302 to a signed blob URL; HttpClient follows it and
            // strips the Authorization header cross-origin automatically, so the token never leaves GitHub's API host.
            using var zipResp = await http.GetAsync(
                $"repos/{options.Owner}/{options.Repo}/actions/artifacts/{artifactId}/zip", ct);
            if (!zipResp.IsSuccessStatusCode)
                return Result<BackupArtifact>.Failure(FriendlyMessage(zipResp.StatusCode, "download the backup"), (int)zipResp.StatusCode);

            var bytes = await zipResp.Content.ReadAsByteArrayAsync(ct);
            return Result<BackupArtifact>.Success(new BackupArtifact($"{name}.zip", bytes, "application/zip"));
        }
        catch (Exception)
        {
            return Result<BackupArtifact>.Failure("Could not reach the backup service. Please try again.", 502);
        }
    }

    /// <summary>Fetch workflow runs and map them into DTOs; null on a non-success response.</summary>
    private async Task<IReadOnlyList<BackupRunDto>?> FetchRunsAsync(int count, CancellationToken ct)
    {
        var runsEl = await FetchRunsRawAsync(count, ct);
        if (runsEl is null)
            return null;

        var list = new List<BackupRunDto>();
        foreach (var run in runsEl.Value.EnumerateArray())
        {
            var id = run.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var idVal) ? idVal : 0L;
            var status = run.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? st.GetString() ?? "unknown" : "unknown";
            var conclusion = run.TryGetProperty("conclusion", out var cc) && cc.ValueKind == JsonValueKind.String
                ? cc.GetString() : null;
            var createdAt = run.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String
                && DateTime.TryParse(ca.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var caVal)
                ? caVal : DateTime.MinValue;
            var evt = run.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String
                ? ev.GetString() : null;
            var htmlUrl = run.TryGetProperty("html_url", out var hu) && hu.ValueKind == JsonValueKind.String
                ? hu.GetString() ?? string.Empty : string.Empty;

            list.Add(new BackupRunDto(id, status, conclusion, createdAt, evt, htmlUrl));
        }

        return list;
    }

    /// <summary>Raw workflow_runs array (cloned so it outlives the JsonDocument); null on non-success.</summary>
    private async Task<JsonElement?> FetchRunsRawAsync(int count, CancellationToken ct)
    {
        var url = $"repos/{options.Owner}/{options.Repo}/actions/workflows/{options.WorkflowFileName}/runs?per_page={count}";
        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            return JsonDocument.Parse("[]").RootElement.Clone();

        return runs.Clone();
    }

    /// <summary>Read a string property defensively; null when absent or not a JSON string.</summary>
    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>Read a UTC timestamp property defensively; null when absent or unparseable.</summary>
    private static DateTime? ReadDate(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
        && DateTime.TryParse(p.GetString(), null,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var val)
            ? val : null;

    private static string FriendlyMessage(HttpStatusCode code, string action) => code switch    {
        HttpStatusCode.Unauthorized => $"The backup service is not authorized to {action}.",
        HttpStatusCode.Forbidden => $"The backup service is not permitted to {action}.",
        HttpStatusCode.NotFound => "The backup workflow or artifact could not be found.",
        _ => $"Could not {action} right now. Please try again."
    };
}
