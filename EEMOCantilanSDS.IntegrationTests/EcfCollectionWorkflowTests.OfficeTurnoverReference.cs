using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>The internal turnover reference is StallTrack's own (OCT-year-number); a supporting document reference is optional and separate.</summary>
public sealed partial class EcfCollectionWorkflowTests
{
    [SkippableFact]
    public async Task OfficeTurnover_gets_a_system_reference_without_any_entered_reference_and_keeps_an_entered_one_separate()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);

        var first = await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.FirstId], 600m, reference: null));
        Assert.True(first.IsSuccess, first.Error);
        var second = await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.SecondId], 600m, reference: "SLIP-77"));
        Assert.True(second.IsSuccess, second.Error);

        Assert.Matches($@"^OCT-{PhilippineTime.Today.Year}-\d{{6,}}$", first.Value!.Row.ReferenceCode);
        Assert.Null(first.Value.Row.Reference);
        Assert.Equal("SLIP-77", second.Value!.Row.Reference);                                   // the entered reference is never the system one
        Assert.NotEqual(first.Value.Row.ReferenceCode, second.Value.Row.ReferenceCode);
        Assert.DoesNotContain("SLIP", second.Value.Row.ReferenceCode);

        var retry = await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [o.SecondId], 600m, reference: "SLIP-77"));
        Assert.False(retry.IsSuccess);                                                          // already covered: no second turnover, no second reference
    }

    [SkippableFact]
    public async Task OfficeTurnover_references_stay_unique_when_turnovers_are_recorded_at_the_same_time()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var (c, o) = await ArrangeOfficeAsync();
        var results = await Task.WhenAll(new[] { o.FirstId, o.SecondId }.Select(async id =>
        {
            await using var ctx = db.CreateContext(c.Seed.TenantId);
            return await Turnover(ctx, c).RecordAsync(Request(Guid.NewGuid(), [id], 600m, reference: null));
        }));
        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(2, results.Select(r => r.Value!.Row.ReferenceCode).Distinct().Count());
    }

    [SkippableFact]
    public async Task Upgrading_keeps_an_existing_turnover_intact_and_gives_it_a_system_reference()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var name = "compat_" + Guid.NewGuid().ToString("N")[..8];
        await using (var admin = db.CreateRawConnection())
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }
        var cs = new NpgsqlConnectionStringBuilder(db.CreateRawConnection().ConnectionString) { Database = name, Pooling = false }.ConnectionString;
        try
        {
            await using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).Options, new FixedTenant(Guid.Empty));
            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync("20261018091035_AddOfficeCashTurnover20261018");
            var tenant = Guid.NewGuid();
            await ctx.Database.ExecuteSqlRawAsync("""
                SET session_replication_role = replica;
                INSERT INTO "OfficeCashTurnovers" ("Id","MunicipalityId","TurnoverDate","PeriodFrom","PeriodTo","ExpectedAmount","TurnedOverAmount","DifferenceAmount",
                    "Reference","TurnedOverBy","Status","ClientOperationId","IntentFingerprint","RecordedAtUtc","RecordedBy","RecordedByActorId","CollectionCount")
                VALUES (gen_random_uuid(), {0}, DATE '2026-10-18', DATE '2026-10-17', DATE '2026-10-18', 200, 200, 0,
                    'SYNTH-TURNOVER-001', 'Head Cashier', 1, gen_random_uuid(), 'fp', now(), 'Head', 'actor', 2);
                """, tenant);
            await migrator.MigrateAsync();

            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync();
            await using var read = new NpgsqlCommand("""SELECT "Reference","ReferenceCode","ReferenceYear","ExpectedAmount" FROM "OfficeCashTurnovers" """, conn);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("SYNTH-TURNOVER-001", reader.GetString(0));                            // history untouched
            Assert.Matches(@"^OCT-2026-\d{6,}$", reader.GetString(1));
            Assert.Equal((2026, 200m), (reader.GetInt32(2), reader.GetDecimal(3)));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await using var admin = db.CreateRawConnection();
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
