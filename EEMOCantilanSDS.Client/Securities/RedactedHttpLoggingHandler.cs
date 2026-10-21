using System.Diagnostics;
using EEMOCantilanSDS.Application.Common.Security;

namespace EEMOCantilanSDS.Client.Securities;

/// <summary>
/// The only request logging the portal's API clients do. It replaces the framework's per-client handler, which prints the
/// full request URI - and an activation, reset or verification lookup carries its one-time credential in that URI.
/// </summary>
/// <remarks>
/// Logs method, the route with credentials redacted, status and duration: enough to tell which call is slow or failing,
/// nothing that can be replayed. Failures are warnings; the request and response bodies are never read.
/// </remarks>
public sealed class RedactedHttpLoggingHandler(ILogger<RedactedHttpLoggingHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var route = SensitiveRoutes.Redact(request.RequestUri);
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            var level = response.IsSuccessStatusCode ? LogLevel.Information : LogLevel.Warning;
            logger.Log(level, "API {Method} {Route} -> {Status} in {ElapsedMs} ms", request.Method.Method, route, (int)response.StatusCode, clock.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The exception TYPE only: some transport exception messages repeat the request URI.
            logger.LogWarning("API {Method} {Route} failed after {ElapsedMs} ms ({ExceptionType})", request.Method.Method, route, clock.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }
}
