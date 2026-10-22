using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.Testing;

public sealed class FishMeatRegistrationRequestSchemaTests(ITestOutputHelper output)
{
    [Fact]
    public void Promoted_migration_adds_only_the_reviewed_request_table_and_indexes()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused").Options;
        using var active = new AppDbContext(options);
        var proposed = active.GetService<IDesignTimeModel>().Model;
        Assert.NotNull(proposed.FindEntityType(typeof(FishMeatRegistrationRequest)));
        var assembly = active.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(
            assembly.Migrations.Single(x => x.Key == "20261020133853_AddFishMeatRegistrationRequestsWorkingCopy20261020").Value,
            active.Database.ProviderName!);
        var operations = migration.UpOperations;
        var table = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("FishMeatRegistrationRequests", table.Name);
        Assert.All(operations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation, op.GetType().Name));
        Assert.Equal(4, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys, fk => Assert.Equal("MunicipalityId", fk.Columns[0]));
        Assert.All(table.ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        var commands = active.GetService<IMigrationsSqlGenerator>().Generate(operations, proposed);
        foreach (var command in commands)
        {
            Assert.False(command.TransactionSuppressed); output.WriteLine(command.CommandText);
        }
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var artifact = File.ReadAllLines(Path.Combine(root, "docs/evidence/fish-meat-registration-requests/request-schema.sql"));
        static string Normalize(string value) => string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(Normalize(string.Join("\n", commands.Select(x => x.CommandText))),
            Normalize(string.Join("\n", artifact.Where(x => !x.StartsWith("--")))));
    }

}
