using EEMOCantilanSDS.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// An unhandled exception on a one-time-link route is logged with its method and path. The path carries the link's credential,
/// so it must be logged redacted: the failure stays diagnosable (method, route shape, trace id) and the credential is never in the log.
/// </summary>
public sealed class ExceptionLoggingRedactsCredentialsTests
{
    private const string Secret = "tkn_Zq81-ExampleOneTimeCredential_7f3a9c0d";

    private sealed class Lines : ILogger<ExceptionHandlingMiddleware>
    {
        public readonly List<string> Entries = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception) + " " + exception);
    }

    [Theory]
    [InlineData("/api/activation/context/" + Secret, "/api/activation/context/[redacted]")]
    [InlineData("/api/onboarding/" + Secret + "/submit", "/api/onboarding/[redacted]/submit")]
    [InlineData("/api/AdminAuth/reset-context/" + Secret, "/api/AdminAuth/reset-context/[redacted]")]
    [InlineData("/api/mobile/bind/" + Secret, "/api/mobile/bind/[redacted]")]
    public async Task AnUnhandledFailureOnALinkRoute_IsLoggedWithoutTheCredential(string path, string expectedLogged)
    {
        var log = new Lines();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new InvalidOperationException("boom"), log);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        var entry = Assert.Single(log.Entries);
        Assert.True(entry.Contains("GET " + expectedLogged), entry);
        Assert.DoesNotContain(Secret, entry);
        Assert.Contains("TraceId", entry);
    }
}
