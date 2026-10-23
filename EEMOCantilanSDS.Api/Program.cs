using EEMOCantilanSDS.Api;
using EEMOCantilanSDS.Api.Extensions;
using EEMOCantilanSDS.Api.Middleware;
using EEMOCantilanSDS.Application;
using EEMOCantilanSDS.Infrastructure;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

// The digest-pinned release runner performs EF checks and migration before web-host setup.
// Do not resolve PayMongo, JWT, background processors, tenant defaults, or seeders here.
var releaseMigrationMode = Environment.GetEnvironmentVariable("STALLTRACK_MIGRATION_MODE");
if (releaseMigrationMode is not null)
{
    var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    if (string.IsNullOrWhiteSpace(connection))
        throw new InvalidOperationException("A production migration connection string is required.");
    using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
    var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options;
    await using var db = new AppDbContext(options);
    await DatabaseStartup.RunReleaseMigrationsAsync(db,
        loggerFactory.CreateLogger("StallTrackMigration"), releaseMigrationMode);
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Structured logging (Hardening §7 — observability): emit machine-parseable JSON
// to stdout in non-development so the App Service log stream / any log collector can
// query errors by property (TraceId, Method, Path). Uses the built-in JSON console
// formatter — no external logging dependency, works on the current App Service tier.
// Development keeps the default readable console output.
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    });
}

builder.Services.AddApi(builder.Environment,builder.Configuration);
builder.Services.AddInfrastructureService(builder.Configuration);
builder.Services.AddApplicationService();
builder.Services.AddPreviewMonthlyRentReceipts(builder.Configuration);
builder.Services.AddWorkingCopyOfficeCashTurnover(builder.Configuration);
builder.Services.AddWorkingCopyNpmMonthly(builder.Configuration, builder.Environment);
builder.Services.AddWorkingCopyNpmAmounts(builder.Configuration, builder.Environment);
builder.Services.AddWorkingCopyFishMeatRegistrationRequests(builder.Configuration, builder.Environment);
builder.Services.AddFishMeatRequestActivation(builder.Configuration);
builder.ConfigureServices();



var app = builder.Build();

await app.VerifyWorkingCopyNpmMonthlyAsync();
await app.VerifyWorkingCopyFishMeatRegistrationRequestsAsync();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsAtStartup"))
{
    await DatabaseStartup.MigrateAndSeedAsync(app.Services, app.Logger);
}

// Resolve the default LGU once, for the token-less callers that legitimately have no tenant: login,
// activation, the payment webhook, background work and startup itself.
await DatabaseStartup.ResolveDefaultTenantAsync(app.Services, app.Logger);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
    app.UseMiddleware<SecurityHeadersMiddleware>();
}


app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Strict,
    HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.Always
});

app.UseRouting();

app.UseRateLimiter();

app.UseCors("AllowClient");

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();

app.UseAuthorization();

// After authentication and authorization, so the claim is available and the endpoint's own [Authorize] has already had its
// say: an anonymous caller gets the ordinary 401 rather than this. Before MapControllers, so a blocked session reaches no
// handler at all.
app.UseMiddleware<MustChangePasswordMiddleware>();

// The operator may reach the platform's own endpoints and nothing belonging to a municipality. Sits here, after authorisation, for
// the same reason its sibling above does: the claim it reads is only present once the token has been validated. Ordinary LGU
// accounts carry no operator claim and pass straight through.
app.UseMiddleware<PlatformOperatorBoundaryMiddleware>();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
// Readiness probe: confirms the database is reachable (for load-balancer/monitoring readiness checks).
// Liveness (/health) stays dependency-free so a transient DB blip doesn't restart the app.
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "ready" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
   .AllowAnonymous();

app.MapHub<EEMOCantilanSDS.Api.Hubs.OnlinePaymentHub>("/hubs/online-payments");
app.MapHub<EEMOCantilanSDS.Api.Hubs.PayorNotificationHub>("/hubs/payor");

app.Run();
