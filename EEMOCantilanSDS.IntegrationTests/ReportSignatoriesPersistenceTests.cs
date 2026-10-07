using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>The office's Prepared by / Certified Correct lines must actually reach the database, not only change an in-memory entity.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ReportSignatoriesPersistenceTests(PostgresFixture database)
{
    private async Task<Municipality> SeedAsync()
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "");
        await database.ResetAsync();
        var tenant = Municipality.Create("SIG-" + Guid.NewGuid().ToString("N")[..8], "Signatory test", "Province", MunicipalityStatus.Active,
            tenantCode: "sig-" + Guid.NewGuid().ToString("N")[..8]);
        await using var setup = database.CreateContext(Guid.Empty);
        setup.Add(tenant);
        await setup.SaveChangesAsync();
        return tenant;
    }

    [SkippableFact]
    public async Task SavedSignatoriesAreWrittenToTheDatabase_OnlyWhenTheUntrackedMunicipalityIsMarkedChanged()
    {
        var tenant = await SeedAsync();
        const string json = "{\"Align\":\"left\",\"Lines\":[{\"Caption\":\"Prepared by\",\"Name\":\"A. Aide\",\"Title\":\"Admin. Aide III\"}]}";

        // The lookup is untracked: setting the value alone changes memory and nothing else.
        await using (var forgotten = database.CreateContext(Guid.Empty))
        {
            var repository = new MunicipalityRepository(forgotten);
            var loaded = (await repository.GetByIdentifierAsync(tenant.TenantCode, default))!;
            loaded.SetReportSignatories(json, "head");
            await new UnitOfWork(forgotten).SaveChangesAsync(default);
        }
        await using (var check = database.CreateContext(Guid.Empty))
            Assert.Null((await check.Municipalities.AsNoTracking().SingleAsync(x => x.Id == tenant.Id)).ReportSignatories);

        await using (var marked = database.CreateContext(Guid.Empty))
        {
            var repository = new MunicipalityRepository(marked);
            var loaded = (await repository.GetByIdentifierAsync(tenant.TenantCode, default))!;
            loaded.SetReportSignatories(json, "head");
            repository.MarkChanged(loaded);
            await new UnitOfWork(marked).SaveChangesAsync(default);
        }
        await using var verify = database.CreateContext(Guid.Empty);
        var stored = (await verify.Municipalities.AsNoTracking().SingleAsync(x => x.Id == tenant.Id)).ReportSignatories;
        Assert.Contains("A. Aide", stored);
        Assert.Contains("Admin. Aide III", stored);
    }
}
