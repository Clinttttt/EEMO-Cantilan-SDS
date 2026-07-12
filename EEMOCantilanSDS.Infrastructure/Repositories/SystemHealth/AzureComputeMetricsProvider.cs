using System.Net.Http.Headers;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using Microsoft.Extensions.Configuration;

namespace EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;

/// <summary>
/// Reads the database server's host CPU %, memory % and provisioned storage. PostgreSQL cannot report
/// host metrics, so CPU/memory come from Azure Monitor's REST API, authenticated with the API app's
/// user-assigned managed identity via the App Service token endpoint (no SDK, no secrets). Results are
/// cached briefly (the panel polls every ~5s; Azure Monitor is queried at most once/minute) and every
/// step degrades to null on any failure so the panel shows "—" rather than a fabricated value.
/// </summary>
public sealed class AzureComputeMetricsProvider(HttpClient http, IConfiguration config) : IComputeMetricsProvider
{
    private static readonly object Gate = new();
    private static double? _cpu;
    private static double? _mem;
    private static DateTime _cachedAtUtc = DateTime.MinValue;
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    public async Task<ComputeMetrics> GetAsync(CancellationToken ct)
    {
        var provisioned = long.TryParse(config["DatabaseHealth:ProvisionedStorageBytes"], out var pb) ? pb : 0L;

        lock (Gate)
        {
            if (DateTime.UtcNow - _cachedAtUtc < CacheFor)
                return new ComputeMetrics(_cpu, _mem, provisioned);
        }

        double? cpu = null, mem = null;
        try
        {
            var resourceId = config["DatabaseHealth:AzureResourceId"];
            var clientId = config["DatabaseHealth:ManagedIdentityClientId"];
            var identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
            var identityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER");

            if (!string.IsNullOrWhiteSpace(resourceId)
                && !string.IsNullOrWhiteSpace(identityEndpoint)
                && !string.IsNullOrWhiteSpace(identityHeader))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));

                var token = await GetTokenAsync(identityEndpoint!, identityHeader!, clientId, timeout.Token);
                if (!string.IsNullOrWhiteSpace(token))
                    (cpu, mem) = await QueryMetricsAsync(resourceId!, token!, timeout.Token);
            }
        }
        catch
        {
            // Any failure (no permission, timeout, transient) → keep last-good / null.
        }

        lock (Gate)
        {
            if (cpu is not null || mem is not null)
            {
                _cpu = cpu;
                _mem = mem;
                _cachedAtUtc = DateTime.UtcNow;
            }
            return new ComputeMetrics(cpu ?? _cpu, mem ?? _mem, provisioned);
        }
    }

    private async Task<string?> GetTokenAsync(string endpoint, string header, string? clientId, CancellationToken ct)
    {
        var url = $"{endpoint}?resource=https%3A%2F%2Fmanagement.azure.com%2F&api-version=2019-08-01";
        if (!string.IsNullOrWhiteSpace(clientId))
            url += $"&client_id={clientId}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-IDENTITY-HEADER", header);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }

    private async Task<(double? Cpu, double? Mem)> QueryMetricsAsync(string resourceId, string token, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var from = now.AddMinutes(-15).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var to = now.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var url = $"https://management.azure.com{resourceId}/providers/microsoft.insights/metrics"
                + $"?api-version=2018-01-01&metricnames=cpu_percent,memory_percent&aggregation=Average&interval=PT1M&timespan={from}/{to}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return (null, null);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        double? cpu = null, mem = null;
        if (doc.RootElement.TryGetProperty("value", out var metrics) && metrics.ValueKind == JsonValueKind.Array)
        {
            foreach (var metric in metrics.EnumerateArray())
            {
                var name = metric.TryGetProperty("name", out var n) && n.TryGetProperty("value", out var nv) ? nv.GetString() : null;
                var latest = LatestAverage(metric);
                if (name == "cpu_percent") cpu = latest;
                else if (name == "memory_percent") mem = latest;
            }
        }
        return (cpu, mem);
    }

    // Metric data points are chronological — take the most recent non-null average.
    private static double? LatestAverage(JsonElement metric)
    {
        if (!metric.TryGetProperty("timeseries", out var series) || series.ValueKind != JsonValueKind.Array)
            return null;

        double? value = null;
        foreach (var ts in series.EnumerateArray())
        {
            if (!ts.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) continue;
            foreach (var point in data.EnumerateArray())
                if (point.TryGetProperty("average", out var avg) && avg.ValueKind == JsonValueKind.Number)
                    value = avg.GetDouble();
        }
        return value;
    }
}
