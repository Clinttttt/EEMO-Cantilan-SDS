using System.Net;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Common.Security;
using EEMOCantilanSDS.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.ComponentTests.Services;

/// <summary>
/// One-time links (Head activation, Admin invitation, password reset, e-mail verification, onboarding, device bind) carry their
/// credential in the URL path. Whatever the portal logs about the calls those links trigger must not contain it, while method,
/// route shape, status and timing stay available.
/// </summary>
public sealed class TokenLoggingTests
{
    // Not real credentials; long enough and distinctive enough that any leak is a plain substring match.
    private const string Secret = "tkn_Zq81-ExampleOneTimeCredential_7f3a9c0d";

    [Theory]
    [InlineData("/api/activation/context/" + Secret, "/api/activation/context/[redacted]")]
    [InlineData("/api/AdminAuth/reset-context/" + Secret, "/api/AdminAuth/reset-context/[redacted]")]
    [InlineData("/api/mobile/bind/" + Secret, "/api/mobile/bind/[redacted]")]
    [InlineData("/api/onboarding/" + Secret, "/api/onboarding/[redacted]")]
    [InlineData("/api/onboarding/" + Secret + "/submit", "/api/onboarding/[redacted]/submit")]
    [InlineData("/activate/" + Secret, "/activate/[redacted]")]
    [InlineData("/reset-password/" + Secret + "?lgu=carrascal", "/reset-password/[redacted]?lgu=carrascal")]
    [InlineData("/verify-email/" + Secret, "/verify-email/[redacted]")]
    [InlineData("/onboarding/" + Secret, "/onboarding/[redacted]")]
    [InlineData("/somewhere?token=" + Secret + "&lgu=x", "/somewhere?token=[redacted]&lgu=x")]
    [InlineData("/somewhere?a=1&Code=" + Secret, "/somewhere?a=1&Code=[redacted]")]
    public void ACredentialInARouteIsReplaced_AndTheRouteShapeIsKept(string input, string expected)
    {
        var redacted = SensitiveRoutes.Redact(input);
        Assert.Equal(expected, redacted);
        Assert.DoesNotContain(Secret, redacted);
    }

    [Theory]
    [InlineData("/api/onboarding/by-request/3f2b2a0c-5d1e-4c0e-9d57-0a1b2c3d4e5f/approve-validation")]
    [InlineData("/api/operation-settings/ECF")]
    [InlineData("/operations/ecf/statements?year=2026&month=10")]
    [InlineData("/api/utility-register?kind=1&year=2026&month=10")]
    [InlineData("")]
    public void OrdinaryRoutesAreLoggedAsTheyAre(string input) => Assert.Equal(input, SensitiveRoutes.Redact(input));

    [Fact]
    public void AFullUriKeepsItsHostAndLosesItsCredential()
    {
        var text = SensitiveRoutes.Redact(new Uri("http://127.0.0.1:7497/api/activation/context/" + Secret));
        Assert.Equal("http://127.0.0.1:7497/api/activation/context/[redacted]", text);
    }

    private sealed class Capture : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        public readonly List<string> Lines = [];
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
        public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);
        public void Dispose() { }

        private sealed class Sink(Capture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var scopes = new List<string>();
                owner._scopes.ForEachScope((s, l) => l.Add(s?.ToString() ?? string.Empty), scopes);
                lock (owner.Lines)
                    owner.Lines.Add($"{category} | {formatter(state, exception)} | {string.Join(" / ", scopes)} | {exception}");
            }
        }
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }

    private static ServiceProvider Portal(Capture capture, HttpStatusCode status)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ApiBaseUrl"] = "http://127.0.0.1:7497" }).Build();
        var services = new ServiceCollection();
        // Every category at Information, like the portal's own default configuration.
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));
        services.AddApiHttpClients(config);
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => new StubHandler(status)));
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task TheLinkLookupsNeverLogTheirCredential_ButDoLogTheCallForDiagnosis(HttpStatusCode status)
    {
        var capture = new Capture();
        await using var portal = Portal(capture, status);

        await portal.GetRequiredService<IActivationApiClient>().GetActivationContextAsync(Secret);
        await portal.GetRequiredService<IAuthApiClient>().GetPasswordResetContextAsync(Secret);

        Assert.NotEmpty(capture.Lines);
        Assert.DoesNotContain(capture.Lines, line => line.Contains(Secret, StringComparison.Ordinal));
        Assert.DoesNotContain(capture.Lines, line => line.Contains(Uri.EscapeDataString(Secret), StringComparison.Ordinal));
        Assert.Contains(capture.Lines, l => l.Contains("API GET http://127.0.0.1:7497/api/activation/context/[redacted] -> " + (int)status));
        Assert.Contains(capture.Lines, l => l.Contains("/api/AdminAuth/reset-context/[redacted]"));
        // The framework's own per-request logger (which prints the URI) is no longer in the pipeline.
        Assert.DoesNotContain(capture.Lines, l => l.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal));
    }
}
