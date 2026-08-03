using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// A throwaway PostgreSQL for the integration tests: one container per test run, the EF migrations applied to a
/// blank database, and the container dropped afterwards. Nothing to install, nothing to restore by hand, and no
/// database on the machine is touched.
///
/// <para>Why a real PostgreSQL rather than an in-memory provider: what these tests are for is the SQL our
/// repositories actually emit — predicates that decide which rows the office sees, the tenant query filter, and
/// the column types money is stored in. An in-memory provider answers in LINQ, so it cannot fail the way
/// production fails.</para>
///
/// <para>It needs a Docker-compatible runtime. When there is none — a machine with the daemon stopped, or a
/// runner without containers — the fixture records why and every test that depends on it reports as SKIPPED
/// rather than failing or, worse, passing while asserting nothing.</para>
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Built inside InitializeAsync, not here: resolving the Docker endpoint happens during Build(), and a field
    // initialiser that throws takes the whole collection down as an error — the one outcome this fixture exists
    // to avoid. Constructed and started under the same guard, a stopped daemon becomes a stated skip.
    private PostgreSqlContainer? _postgres;

    /// <summary>Null when the database came up; otherwise why the tests must skip.</summary>
    public string? UnavailableReason { get; private set; }

    public bool Available => UnavailableReason is null && _postgres is not null;

    private string ConnectionString => _postgres?.GetConnectionString()
        ?? throw new InvalidOperationException(UnavailableReason ?? "The test database was never started.");

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                // Pinned: a test that only passes on whatever "latest" happens to be is not a test of anything.
                .WithImage("postgres:16-alpine")
                .WithDatabase("stalltrack_tests")
                .WithUsername("stalltrack")
                .WithPassword("stalltrack")
                .WithCleanUp(true)
                .Build();

            await _postgres.StartAsync();
        }
        catch (Exception ex)
        {
            _postgres = null;
            UnavailableReason =
                "A Docker-compatible runtime is required to start the test database (" +
                ex.GetType().Name + ": " + FirstLine(ex.Message) + "). " +
                "Start Docker and run the tests again.";
            return;
        }

        // Outside the guard on purpose: a migration that will not apply is a real failure and must fail the
        // run, not be reported as "no container runtime" and skipped.
        await using var context = CreateContext(Guid.Empty);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is null) return;
        try { await _postgres.DisposeAsync(); }
        catch { /* the container is throwaway; a failure to remove it must not fail the run */ }
    }

    /// <summary>
    /// A context on the throwaway database, reading as the given tenant. <see cref="Guid.Empty"/> reads across
    /// tenants, which is how the platform operator sees it.
    /// </summary>
    public AppDbContext CreateContext(Guid municipalityId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            // The same interceptor the app runs with, so a row inserted here is attributed to a municipality
            // exactly as it would be in production. Without it every seeded row would be tenant-less, and a
            // tenant-scoped read would find nothing — the test would "prove" isolation that isn't there.
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;

        return new AppDbContext(options, new FixedMunicipality(municipalityId));
    }

    /// <summary>
    /// Empties every table the tests write to, so each test starts from a stated position instead of inheriting
    /// whatever the previous one left. Cheaper and clearer than a container per test.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var context = CreateContext(Guid.Empty);
        await context.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE "UtilityBills", "Contracts", "Stalls", "Facilities", "Municipalities" CASCADE;
            """);
    }

    private static string FirstLine(string message) =>
        message.Split('\n')[0].Trim();

    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }
}

/// <summary>One container for the whole run: starting PostgreSQL per test class would cost more than it proves.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
