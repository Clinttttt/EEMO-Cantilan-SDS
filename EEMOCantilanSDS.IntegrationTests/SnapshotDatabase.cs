using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Connects the integration tests to an ISOLATED RESTORED SNAPSHOT of production — never the live database.
///
/// <para>Set <c>STALLTRACK_SNAPSHOT_DB</c> to the snapshot's connection string to run them:</para>
/// <code>setx STALLTRACK_SNAPSHOT_DB "Host=localhost;Database=stalltrack_snapshot;Username=…;Password=…"</code>
///
/// <para>Without that variable every test here reports as skipped, so CI and a developer machine stay green and
/// nobody is ever pointed at production by accident. Two further guards refuse to run: a connection string that
/// names a host outside the local machine, and one whose database name does not look like a snapshot. Every query
/// in this project is read-only — the context is created with tracking disabled and nothing calls SaveChanges.</para>
/// </summary>
public static class SnapshotDatabase
{
    public const string EnvironmentVariable = "STALLTRACK_SNAPSHOT_DB";

    /// <summary>The snapshot connection string, or null when the tests should skip.</summary>
    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } value ? value : null;

    /// <summary>True when a usable, safe-looking snapshot is configured.</summary>
    public static bool Available => ConnectionString is { } cs && IsSafeTarget(cs);

    /// <summary>
    /// Why the tests are skipped, for the assertion message — so a skipped run explains itself rather than looking
    /// like a pass.
    /// </summary>
    public static string SkipReason =>
        ConnectionString is null
            ? $"Set {EnvironmentVariable} to a RESTORED SNAPSHOT connection string to run the integration tests."
            : "The configured database does not look like an isolated snapshot (expected a local host and a name "
              + "containing 'snapshot', 'restore' or 'test'), so the tests were skipped rather than risk the live database.";

    /// <summary>
    /// A read-only context over the snapshot. <paramref name="municipalityId"/> selects the tenant to read as;
    /// <see cref="Guid.Empty"/> reads across tenants (the platform-operator view).
    /// </summary>
    public static AppDbContext OpenRead(Guid municipalityId)
    {
        if (ConnectionString is not { } cs || !IsSafeTarget(cs))
            throw new InvalidOperationException(SkipReason);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        return new AppDbContext(options, new FixedMunicipality(municipalityId));
    }

    /// <summary>
    /// Refuses anything that does not look like a local, clearly-named snapshot. Deliberately conservative: the
    /// cost of a false negative is a skipped test, the cost of a false positive is querying production.
    /// </summary>
    private static bool IsSafeTarget(string connectionString)
    {
        var lower = connectionString.ToLowerInvariant();

        var localHost = lower.Contains("host=localhost")
            || lower.Contains("host=127.0.0.1")
            || lower.Contains("host=::1")
            || lower.Contains("server=localhost")
            || lower.Contains("server=127.0.0.1");

        var snapshotName = lower.Contains("snapshot")
            || lower.Contains("restore")
            || lower.Contains("_test");

        return localHost && snapshotName;
    }

    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }
}
