using EEMOCantilanSDS.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class NpmRentMonthFinancialTests
{
    [SkippableFact]
    public async Task Financial_schema_empty_database_supports_transactional_down_up_and_rollback()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync();
        await using var db = fixture.Create(Guid.Empty);
        var migration = new AddNpmRentMonthFinancialSource20261020();
        var generator = db.GetService<IMigrationsSqlGenerator>();
        var model = db.GetService<IDesignTimeModel>().Model;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            foreach (var operation in new[] { migration.DownOperations, migration.UpOperations })
                foreach (var command in generator.Generate(operation, model))
                {
                    Assert.False(command.TransactionSuppressed);
                    await db.Database.ExecuteSqlRawAsync(command.CommandText);
                }
            await transaction.RollbackAsync();
        }
        _ = await db.NpmRentMonths.Select(x => new { x.SettlementActivatedAtUtc, x.SettlementActivatedByUserId, x.SettlementSealedAtUtc }).ToListAsync();
        _ = await db.CollectionAllocations.Select(x => x.NpmRentMonthId).ToListAsync();
        output.WriteLine("Real isolated PostgreSQL: additive Up, empty-only Down, reapply and rollback preserve the installed financial schema.");
    }

    [SkippableFact]
    public async Task Financial_schema_down_refuses_to_discard_prepared_or_posted_evidence()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync();
        var world = await SeedOpening(new(2026, 10, 20));
        await using var db = fixture.Create(world.World.Tenant);
        var prepared = await OpeningWorkflow(db, world).PrepareOpeningMonthAsync(world.World.Stall, world.Contract, new(2026, 10, 1));
        Assert.True(prepared.IsSuccess, prepared.Error);
        var migration = new AddNpmRentMonthFinancialSource20261020();
        var guard = db.GetService<IMigrationsSqlGenerator>().Generate(migration.DownOperations, db.GetService<IDesignTimeModel>().Model).First();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(guard.CommandText));
            Assert.Contains("NPM financial evidence exists", error.MessageText);
            await transaction.RollbackAsync();
        }
        Assert.Single(await db.NpmRentMonths.ToListAsync());
        Assert.Empty(await db.Collections.ToListAsync());
    }
}
