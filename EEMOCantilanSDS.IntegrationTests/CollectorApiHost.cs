using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EEMOCantilanSDS.Api.Extensions;
using EEMOCantilanSDS.Api.Middleware;
using EEMOCantilanSDS.Application;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Infrastructure;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>Real API controllers, JWT authentication, middleware and production service registrations on scratch PostgreSQL.
/// Only the network transport is TestServer; no workflow, repository, transaction or current-user service is mocked.</summary>
internal sealed class CollectorApiHost(WebApplication app, HttpClient client) : IAsyncDisposable
{
    internal HttpClient Client => client;
    internal IServiceProvider Services => app.Services;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    internal sealed class Fault : SaveChangesInterceptor
    {
        internal Guid? FailAfterPostingOperation { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (FailAfterPostingOperation is { } id && data.Context!.ChangeTracker.Entries<PostingOperation>()
                .Any(x => x.Entity.ClientOperationId == id)) throw new InvalidOperationException("Injected failure after canonical replacement save.");
            return ValueTask.FromResult(result);
        }
    }
    internal static async Task<CollectorApiHost> StartAsync(PostgresFixture database, Guid collector, Guid tenant, Fault? fault = null)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName = typeof(EEMOCantilanSDS.Api.Controllers.MobileCollectionCorrectionsController).Assembly.GetName().Name,
            EnvironmentName = Environments.Development });
        // Never load production connection strings/secrets or run production migrations/backups.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = database.ApiConnectionString,
            ["PayMongo:BaseUrl"] = "https://payments.invalid/v1/",
            ["Jwt:Key"] = key, ["Jwt:Issuer"] = "scratch-api", ["Jwt:Audience"] = "scratch-collector" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        EEMOCantilanSDS.Api.DependencyInjection.AddApi(builder.Services, builder.Environment, builder.Configuration);
        builder.Services.AddInfrastructureService(builder.Configuration);
        builder.Services.AddApplicationService();
        builder.ConfigureServices(); // Real JWT validation, not a test authentication scheme.
        foreach (var service in builder.Services.Where(x => x.ServiceType == typeof(IHostedService)
            && x.ImplementationType == typeof(DailyTenantBackupService)).ToArray()) builder.Services.Remove(service);
        if (fault is not null) builder.Services.AddDbContext<AppDbContext>((_, options) => options.AddInterceptors(fault));
        var app = builder.Build();
        app.UseRouting(); app.UseRateLimiter(); app.UseCors("AllowClient");
        app.UseMiddleware<ExceptionHandlingMiddleware>(); app.UseAuthentication(); app.UseAuthorization();
        app.UseMiddleware<MustChangePasswordMiddleware>(); app.UseMiddleware<PlatformOperatorBoundaryMiddleware>();
        app.MapControllers();
        await app.StartAsync();
        var client = app.GetTestClient();
        var claims = new[] { new Claim(AppClaimTypes.UserId, collector.ToString()), new Claim(AppClaimTypes.Role, "Collector"),
            new Claim(AppClaimTypes.Username, "scratch-collector"), new Claim(AppClaimTypes.FullName, "Scratch Collector"),
            new Claim(AppClaimTypes.MunicipalityId, tenant.ToString()), new Claim(AppClaimTypes.Municipality, "scratch"),
            new Claim(AppClaimTypes.IsActive, "true"), new Claim(AppClaimTypes.MustChangePassword, "false") };
        var token = new JwtSecurityToken("scratch-api", "scratch-collector", claims, expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return new(app, client);
    }
    public async ValueTask DisposeAsync() { client.Dispose(); await app.DisposeAsync(); }
}
