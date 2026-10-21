using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.Testing;

/// <summary>Offline review against the existing migration snapshot. Never connects or executes DDL.</summary>
public sealed class NpmRentMonthSchemaReadinessTests(ITestOutputHelper output)
{
    [Fact]
    public void Promoted_financial_schema_is_additive_nullable_and_matches_the_active_snapshot()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused").Options;
        using var active = new AppDbContext(options);
        using var candidate = new FinancialPreview(options);
        var current = active.GetService<IDesignTimeModel>().Model;
        var proposed = candidate.GetService<IDesignTimeModel>().Model;
        Assert.Empty(active.GetService<IMigrationsModelDiffer>().GetDifferences(current.GetRelationalModel(), proposed.GetRelationalModel()));
        var registry = active.GetService<IMigrationsAssembly>();
        var snapshot = active.GetService<IModelRuntimeInitializer>().Initialize(registry.ModelSnapshot!.Model, true);
        Assert.Empty(active.GetService<IMigrationsModelDiffer>().GetDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel()));
        var operations = registry.CreateMigration(registry.Migrations["20261020050758_AddNpmRentMonthFinancialSource20261020"], active.Database.ProviderName!).UpOperations;
        Assert.Equal(15, operations.Count);
        Assert.Equal(4, operations.OfType<AddColumnOperation>().Count());
        Assert.All(operations.OfType<AddColumnOperation>(), column => {
            Assert.True(column.IsNullable);
            Assert.Contains(column.Table, new[] { "NpmRentMonths", "CollectionAllocations" });
        });
        Assert.All(operations, operation => Assert.True(operation is AddColumnOperation or CreateIndexOperation
            or AddForeignKeyOperation or AddCheckConstraintOperation or DropCheckConstraintOperation, operation.GetType().Name));
        Assert.NotNull(current.FindEntityType(typeof(NpmRentMonth))!.FindProperty(nameof(NpmRentMonth.SettlementActivatedAtUtc)));
        Assert.NotNull(current.FindEntityType(typeof(EEMOCantilanSDS.Domain.Entities.Revenue.CollectionAllocation))!
            .FindProperty("NpmRentMonthId"));
        Assert.Equal(new[] { "MunicipalityId", "NpmRentMonthId" }, operations.OfType<AddForeignKeyOperation>().Single().Columns);
        Assert.False(new EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthRuntime().Allows(Guid.NewGuid()));
        Assert.All(active.GetService<IMigrationsSqlGenerator>().Generate(operations, proposed), command => Assert.False(command.TransactionSuppressed));
    }

    private sealed class FinancialPreview(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            EEMOCantilanSDS.Infrastructure.Persistence.Configuration.NpmRentMonthFinancialSchema.Configure(builder);
        }
    }

    [Fact]
    public void Reviewed_schema_plan_changes_exactly_two_existing_parent_keys_and_one_new_table()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused").Options);
        var registry = context.GetService<IMigrationsAssembly>().Migrations;
        output.WriteLine($"Compiled migration registry: count={registry.Count}; latest={registry.Keys.Max()}");
        var model = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsAssembly>().CreateMigration(
            registry["20261017040000_AddNpmRentMonthFoundation"], context.Database.ProviderName!).UpOperations;

        var keys = operations.OfType<AddUniqueConstraintOperation>().OrderBy(x => x.Table).ToArray();
        Assert.Equal(2, keys.Length);
        Assert.Equal("Facilities", keys[0].Table);
        Assert.Equal("AK_Facilities_MunicipalityId_Id", keys[0].Name);
        Assert.Equal(new[] { "MunicipalityId", "Id" }, keys[0].Columns);
        Assert.Equal("Stalls", keys[1].Table);
        Assert.Equal("AK_Stalls_MunicipalityId_Id_FacilityId", keys[1].Name);
        Assert.Equal(new[] { "MunicipalityId", "Id", "FacilityId" }, keys[1].Columns);

        var table = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("NpmRentMonths", table.Name);
        Assert.Equal(26, table.Columns.Count);
        Assert.Equal(3, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys, fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        Assert.Equal(9, table.CheckConstraints.Count);
        Assert.All(operations.OfType<CreateIndexOperation>(), index => Assert.Equal(table.Name, index.Table));
        Assert.Equal(4, operations.OfType<CreateIndexOperation>().Count());
        Assert.Equal(7, operations.Count); // two parent keys + one table + four indexes
        Assert.DoesNotContain(operations, operation => operation.IsDestructiveChange);
        Assert.True(model.FindEntityType(typeof(NpmRentMonth))!.FindProperty("SettlementVersion")!.IsConcurrencyToken);

        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(operations, model);
        output.WriteLine("OFFLINE DDL REVIEW ONLY; no migration file, connection, execution or snapshot update.");
        foreach (var command in commands) output.WriteLine(command.CommandText);
    }
}
