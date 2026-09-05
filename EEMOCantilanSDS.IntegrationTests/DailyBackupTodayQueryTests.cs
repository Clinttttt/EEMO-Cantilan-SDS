using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The "has today's automated backup been taken?" question must reach the database.
/// </summary>
/// <remarks>
/// This exists because the first version of it did NOT. The obvious spelling -
/// <c>DateOnly.FromDateTime(b.CreatedAtUtc.Add(PhilippineTime.Offset)) == today</c> - reads perfectly and passes every test built on
/// a hand-made context, then fails in production: Npgsql answered "Translation of method 'System.DateTime.Add' failed" and every
/// municipality's backup threw. The service caught it and the platform was unharmed, but nothing was backed up, and the only
/// evidence was six error lines in a container log.
///
/// <para>A unit test could not have caught it, which is the point of putting this here: an EF query is only really tested against
/// the provider that has to translate it. The rule this pins is not "use a range" but "this predicate runs on PostgreSQL".</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class DailyBackupTodayQueryTests(PostgresFixture db)
{
    /// <summary>A Philippine day compared as a UTC range, exactly as the scheduled service asks it.</summary>
    [SkippableFact]
    public async Task TheTodaysAutomatedBackupQueryTranslatesToSql()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");

        var municipalityId = Guid.NewGuid();

        await using var context = db.CreateContext(municipalityId);

        var (startUtc, endUtc) = PhilippineTime.TodayUtcRange();

        // The query the service runs. If it cannot be translated this throws InvalidOperationException, which is the failure this
        // test exists to catch - the result itself matters far less than the fact that PostgreSQL accepted it.
        var alreadyToday = await context.TenantBackups
            .AsNoTracking()
            .Where(b => b.IsAutomated)
            .AnyAsync(b => b.CreatedAtUtc >= startUtc && b.CreatedAtUtc < endUtc);

        Assert.False(alreadyToday);
    }

    /// <summary>
    /// And it answers correctly: an automated backup taken today is found, one taken yesterday is not.
    /// </summary>
    /// <remarks>
    /// Translating is not the same as being right. Without this, a range with the bounds the wrong way round would still translate
    /// and would still have taken a backup every half hour.
    /// </remarks>
    [SkippableFact]
    public async Task ItFindsTodaysAutomatedBackupAndIgnoresYesterdays()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");

        var municipalityId = Guid.NewGuid();
        var (startUtc, endUtc) = PhilippineTime.TodayUtcRange();

        await using (var seed = db.CreateContext(municipalityId))
        {
            // Yesterday, in Philippine terms: one second before this PH day began.
            Add(seed, municipalityId, startUtc.AddSeconds(-1), automated: true);
            await seed.SaveChangesAsync();
        }

        await using (var check = db.CreateContext(municipalityId))
        {
            var found = await check.TenantBackups.AsNoTracking()
                .Where(b => b.IsAutomated)
                .AnyAsync(b => b.CreatedAtUtc >= startUtc && b.CreatedAtUtc < endUtc);

            Assert.False(found);        // yesterday's does not count as today's
        }

        await using (var seed = db.CreateContext(municipalityId))
        {
            Add(seed, municipalityId, startUtc.AddHours(1), automated: true);
            await seed.SaveChangesAsync();
        }

        await using (var check = db.CreateContext(municipalityId))
        {
            var found = await check.TenantBackups.AsNoTracking()
                .Where(b => b.IsAutomated)
                .AnyAsync(b => b.CreatedAtUtc >= startUtc && b.CreatedAtUtc < endUtc);

            Assert.True(found);
        }
    }

    /// <summary>A MANUAL backup taken today must not satisfy the question, or the nightly one would never be taken.</summary>
    [SkippableFact]
    public async Task AManualBackupTodayDoesNotCountAsTheAutomatedOne()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");

        var municipalityId = Guid.NewGuid();
        var (startUtc, endUtc) = PhilippineTime.TodayUtcRange();

        await using (var seed = db.CreateContext(municipalityId))
        {
            Add(seed, municipalityId, startUtc.AddHours(2), automated: false);
            await seed.SaveChangesAsync();
        }

        await using var check = db.CreateContext(municipalityId);

        var found = await check.TenantBackups.AsNoTracking()
            .Where(b => b.IsAutomated)
            .AnyAsync(b => b.CreatedAtUtc >= startUtc && b.CreatedAtUtc < endUtc);

        Assert.False(found);
    }

    private static void Add(DbContext context, Guid municipalityId, DateTime createdAtUtc, bool automated)
    {
        var backup = TenantBackup.Create(
            createdBy: automated ? "system" : "head",
            formatVersion: "restore-v1",
            rowCount: 1,
            tableCount: 1,
            sizeBytes: 10,
            snapshotJson: "{\"FormatVersion\":\"restore-v1\"}",
            note: automated ? "Automated daily backup" : "by hand",
            isAutomated: automated);

        context.Add(backup);
        var entry = context.Entry(backup);
        entry.Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = municipalityId;
        entry.Property(nameof(TenantBackup.CreatedAtUtc)).CurrentValue = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
    }
}
