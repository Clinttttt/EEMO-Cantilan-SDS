using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Testing.Support;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Spins up an isolated in-memory <see cref="AppDbContext"/> per test. EF Core InMemory is used
/// (not SQLite) because these repositories aggregate decimals server-side, which SQLite cannot do;
/// InMemory also honours the global soft-delete query filters. It does not validate Npgsql SQL
/// translation — that is covered by the build and by running against a real database.
///
/// <para>
/// The context arrives with the office's ordinance rates already stated. It used to arrive with none, which
/// worked only because an unstated rate then fell back to the reference municipality's constant — the borrowing
/// that let one LGU bill another's figures. With that gone, an office with no rate rows cannot bill at all, so a
/// repository test about money now says what its office charges. The amounts are the same ones the suite has
/// always expected, so no expectation moves; a test that needs different rates still adds its own rows, which
/// override these by carrying a later effective date or by being read first.
/// </para>
/// </summary>
public abstract class RepositoryTestBase
{
    protected static AppDbContext NewContext()
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        foreach (var entry in TestFeeRates.Entries())
        {
            context.FacilityRates.Add(FacilityRate.Create(
                entry.Facility, entry.Key, entry.Amount, entry.EffectiveDate, createdBy: "test"));
        }
        context.SaveChanges();

        return context;
    }
}
