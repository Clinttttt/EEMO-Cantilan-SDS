using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Persistence.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EEMOCantilanSDS.Infrastructure.Persistence;

/// <summary>
/// The work the application does to its own database before it serves a request: apply migrations, seed reference data, and
/// resolve the default LGU for tenant scoping.
///
/// <para>
/// Owned by Infrastructure because it is entirely about persistence — the migrations, the seeders and the advisory lock are
/// all EF and PostgreSQL concerns. It lived inline in <c>Program.cs</c>, where ninety lines of migration-locking detail sat
/// between the service registrations and the middleware pipeline, and where the lock key was a local constant that the test
/// covering it had to copy.
/// </para>
/// </summary>
public static class DatabaseStartup
{
    /// <summary>
    /// The advisory-lock key that serialises migration across instances. Arbitrary, but shared by every instance of this
    /// application and nothing else. Public so the test that proves the locking behaviour uses the real key rather than its
    /// own transcription of it.
    /// </summary>
    public const long MigrationLockKey = 8_472_113_509_001L;

    /// <summary>How long to wait for another instance to finish migrating before abandoning startup.</summary>
    private const string LockTimeout = "150s";

    /// <summary>
    /// Applies migrations and seeds reference data, with only one instance doing so at a time.
    /// </summary>
    public static async Task MigrateAndSeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            // Rather than waiting for ever behind an instance that has stalled. A migration that genuinely
            // needs longer than this should fail loudly on a deployment, because serving requests against a
            // half-migrated schema is the worse outcome.
            await context.Database.ExecuteSqlRawAsync($"SET lock_timeout = '{LockTimeout}'");
            await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock({0})", MigrationLockKey);

            await context.Database.MigrateAsync();
            await MunicipalitySeeder.SeedAsync(context);
            await FacilitySeeder.SeedAsync(context);
            await FacilityRateSeeder.SeedAsync(context);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            logger.LogCritical(ex,
                "Could not acquire the migration lock within {LockTimeout}. Another instance is still migrating, or one " +
                "stopped while holding it. Startup is being abandoned rather than serving requests against a " +
                "schema that may be half-applied.", LockTimeout);
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
                await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock({0})", MigrationLockKey);
            }
            catch (Exception unlockEx)
            {
                // Not fatal: ending the session releases it too, and the original failure matters more than this.
                logger.LogWarning(unlockEx, "Could not release the migration advisory lock explicitly.");
            }

            await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Resolves the default LGU once, for the token-less callers that legitimately have no tenant of their own: login,
    /// activation, the payment webhook, background work and startup itself.
    ///
    /// <para>
    /// Best-effort by design — a database hiccup here must not take the application down. What it costs when it fails is
    /// stated plainly in the log, and is no longer what the old comment claimed: since the tenant boundary was made to fail
    /// closed, an unresolved default does NOT leave the filter a no-op. Token-less reads return NOTHING and token-less
    /// writes to tenant-owned tables are REFUSED. Login is the visible casualty, which is the opposite of the harmless
    /// degradation the comment used to promise.
    /// </para>
    /// </summary>
    public static async Task ResolveDefaultTenantAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var accessor = scope.ServiceProvider.GetRequiredService<ICurrentMunicipalityAccessor>();

            var defaultMunicipalityId = await db.Municipalities.IgnoreQueryFilters()
                .Where(m => m.IsDefault)
                .Select(m => m.Id)
                .FirstOrDefaultAsync();
            accessor.Set(defaultMunicipalityId);

            if (defaultMunicipalityId == Guid.Empty)
                logger.LogWarning(
                    "Tenant scoping: no default municipality (IsDefault=true) was found at startup. Token-less callers " +
                    "such as login now resolve to NO tenant, so they will read nothing and their writes to tenant-owned " +
                    "tables will be refused until a default municipality exists.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Tenant scoping: failed to resolve the default municipality at startup. Token-less callers such as login " +
                "will read nothing and have their writes refused until the next restart resolves it.");
        }
    }
}
