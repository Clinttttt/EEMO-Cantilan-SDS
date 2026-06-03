using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Spins up an isolated in-memory <see cref="AppDbContext"/> per test. EF Core InMemory is used
/// (not SQLite) because these repositories aggregate decimals server-side, which SQLite cannot do;
/// InMemory also honours the global soft-delete query filters. It does not validate Npgsql SQL
/// translation — that is covered by the build and by running against a real database.
/// </summary>
public abstract class RepositoryTestBase
{
    protected static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
