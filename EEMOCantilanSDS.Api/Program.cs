using EEMOCantilanSDS.Api;
using EEMOCantilanSDS.Api.Extensions;
using EEMOCantilanSDS.Api.Middleware;
using EEMOCantilanSDS.Application;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Infrastructure;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

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
builder.Services.AddApplicationService(builder.Configuration);
builder.ConfigureServices();



var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsAtStartup"))
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    // ── Only one instance may migrate at a time ───────────────────────────────────────────────────
    // Migrating on startup is convenient and, with a single instance, safe. With two - a scale-out, or
    // the overlap while a deployment slot swaps - both would run MigrateAsync against the same database
    // at the same moment, and EF offers no protection: the loser fails on an object the winner has
    // already created, or worse, two half-applied migrations interleave.
    //
    // A PostgreSQL advisory lock serialises them. The second instance waits, acquires the lock, finds
    // nothing pending, and carries on in a second or two. The lock is held on a SESSION, so the
    // connection is opened here and kept open for the duration: were EF to close it between steps, the
    // lock would quietly disappear and the protection would be imaginary.
    //
    // The key is an arbitrary constant, shared by every instance of this application and nothing else.
    const long migrationLockKey = 8_472_113_509_001L;

    var connection = context.Database.GetDbConnection();
    await connection.OpenAsync();
    try
    {
        // Rather than waiting for ever behind an instance that has stalled. A migration that genuinely
        // needs longer than this should fail loudly on a deployment, because serving requests against a
        // half-migrated schema is the worse outcome.
        await context.Database.ExecuteSqlRawAsync("SET lock_timeout = '150s'");
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock({0})", migrationLockKey);

        await context.Database.MigrateAsync();
        await MunicipalitySeeder.SeedAsync(context);
        await FacilitySeeder.SeedAsync(context);
        await FacilityRateSeeder.SeedAsync(context);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
    {
        startupLogger.LogCritical(ex,
            "Could not acquire the migration lock within 150s. Another instance is still migrating, or one " +
            "stopped while holding it. Startup is being abandoned rather than serving requests against a " +
            "schema that may be half-applied.");
        throw;
    }
    finally
    {
        // Released explicitly, on every path, rather than relying on the close below.
        //
        // Closing an Npgsql connection returns it to the POOL; it does not end the PostgreSQL session. An
        // advisory lock belongs to the session, so a lock left held can outlive what looks like closing the
        // connection - proven by MigrationAdvisoryLockTests, where a lock taken in one test was still held in
        // the next after the connection had been disposed. In production that would mean the next deployment
        // waiting 150 seconds and then abandoning startup: an outage caused by the safeguard itself.
        try
        {
            await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", migrationLockKey);
        }
        catch (Exception unlockEx)
        {
            // Not fatal: ending the session releases it too, and the original failure matters more than this.
            startupLogger.LogWarning(unlockEx, "Could not release the migration advisory lock explicitly.");
        }

        await connection.CloseAsync();
    }
}

// Resolve the default municipality once for tenant scoping. Best-effort: the municipality query filter
// is a no-op until this is set, so a DB hiccup here cannot take the app down or hide data.
using (var tenantScope = app.Services.CreateScope())
{
    try
    {
        var db = tenantScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accessor = tenantScope.ServiceProvider
            .GetRequiredService<EEMOCantilanSDS.Application.Common.Tenancy.ICurrentMunicipalityAccessor>();
        var defaultMunicipalityId = await db.Municipalities.IgnoreQueryFilters()
            .Where(m => m.IsDefault)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();
        accessor.Set(defaultMunicipalityId);

        // Surface the unhappy path: an empty default means no seeded default LGU — the tenant filter stays
        // a no-op and token-less writes go unstamped until one exists. Log it so ops can see and correct it.
        if (defaultMunicipalityId == Guid.Empty)
            app.Logger.LogWarning(
                "Tenant scoping: no default municipality (IsDefault=true) was found at startup. The tenant " +
                "filter stays a no-op and token-less writes go unstamped until a default municipality exists.");
    }
    catch (Exception ex)
    {
        // Don't crash startup on a transient DB hiccup (F1 cold-start) — but make the failure visible.
        // The filter stays a no-op until a restart resolves it.
        app.Logger.LogError(ex,
            "Tenant scoping: failed to resolve the default municipality at startup. The tenant filter stays " +
            "a no-op until the next restart resolves it.");
    }
}

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
