using Npgsql;
using Xunit;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The lock that stops two application instances migrating the database at the same moment.
///
/// <para>
/// Migrating at startup is convenient and, with one instance, safe. With two - a scale-out, or the overlap while
/// a deployment slot swaps - both would run MigrateAsync against the same database simultaneously, and EF offers
/// no protection: the loser fails on an object the winner has already created, or two half-applied migrations
/// interleave. Program.cs takes a PostgreSQL advisory lock around the whole migrate-and-seed block.
/// </para>
///
/// <para>
/// These exist because that protection rests on properties of the server, and a lock that does not actually hold
/// is worse than no lock - it replaces a known risk with a false sense of safety. Each test uses its own key: an
/// earlier draft shared one, and a lock left held by one test made the next fail, which is how the pooling
/// hazard below was found in the first place.
/// </para>
///
/// <para>Runs against a throwaway container (see <see cref="PostgresFixture"/>). Skips, stating why, when there
/// is no container runtime available.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class MigrationAdvisoryLockTests(PostgresFixture db)
{
    // The real key the application migrates under, referenced rather than transcribed: a copy that drifted would leave
    // these tests passing against a lock nothing else takes. The tests use offsets of it so that they cannot interfere
    // with each other, or with an application actually starting up against the same server.
    private const long MigrationLockKey = EEMOCantilanSDS.Infrastructure.Persistence.DatabaseStartup.MigrationLockKey;

    [SkippableFact]
    public async Task OnlyOneSessionCanHoldTheMigrationLock()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        var key = MigrationLockKey + 1;

        await using var first = db.CreateRawConnection();
        await first.OpenAsync();
        await using var second = db.CreateRawConnection();
        await second.OpenAsync();

        // The instance that starts first takes the lock and migrates.
        Assert.True(await TryLock(first, key), "the first session should acquire a free lock");

        // The instance that starts second must NOT proceed in parallel. This is the whole point: without it,
        // both would call MigrateAsync against the same schema at the same time.
        Assert.False(await TryLock(second, key), "a second session must not acquire a lock already held");

        await Unlock(first, key);
    }

    [SkippableFact]
    public async Task AnExplicitUnlockReleasesItImmediately()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        var key = MigrationLockKey + 2;

        await using var holder = db.CreateRawConnection();
        await holder.OpenAsync();
        Assert.True(await TryLock(holder, key));

        // What Program.cs does on every path, including when the migration throws. It does NOT rely on closing
        // the connection: closing returns it to the pool rather than ending the PostgreSQL session, and an
        // advisory lock belongs to the session. A lock left held that way would make the next deployment wait
        // its timeout and then abandon startup - an outage caused by the safeguard rather than prevented by it.
        await Unlock(holder, key);

        await using var next = db.CreateRawConnection();
        await next.OpenAsync();
        Assert.True(await TryLock(next, key), "an explicitly released lock must be free at once");
        await Unlock(next, key);
    }

    [SkippableFact]
    public async Task TheLockSurvivesTheWorkDoneWhileHoldingIt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        var key = MigrationLockKey + 3;

        // The subtlety that makes the protection real. The lock must cover the WHOLE migration, not just the
        // statement that took it, so Program.cs opens the connection itself and keeps it open throughout - were
        // EF to close and reopen it between steps, the lock would quietly disappear.
        await using var holder = db.CreateRawConnection();
        await holder.OpenAsync();
        await using var rival = db.CreateRawConnection();
        await rival.OpenAsync();

        Assert.True(await TryLock(holder, key));

        // Work of the kind a migration does, on the same open session.
        await Execute(holder, "CREATE TABLE IF NOT EXISTS lock_drill (id int primary key)");
        await Execute(holder, "INSERT INTO lock_drill (id) VALUES (1) ON CONFLICT DO NOTHING");
        await Execute(holder, "DROP TABLE lock_drill");

        Assert.False(await TryLock(rival, key), "the lock must still be held after the session has done its work");

        await Unlock(holder, key);
    }

    [SkippableFact]
    public async Task ADifferentKeyIsADifferentLock_WhichIsWhyTheKeyMustBeShared()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        var key = MigrationLockKey + 4;

        // Guards against a typo in the key doing nothing visible: two instances using different keys would both
        // migrate, and everything would look fine until the day it did not.
        await using var first = db.CreateRawConnection();
        await first.OpenAsync();
        await using var second = db.CreateRawConnection();
        await second.OpenAsync();

        Assert.True(await TryLock(first, key));
        Assert.True(await TryLock(second, key + 1), "a different key does not serialise anything");

        await Unlock(first, key);
        await Unlock(second, key + 1);
    }

    private static async Task<bool> TryLock(NpgsqlConnection connection, long key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        command.Parameters.AddWithValue("key", key);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Unlock(NpgsqlConnection connection, long key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@key)";
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteScalarAsync();
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
