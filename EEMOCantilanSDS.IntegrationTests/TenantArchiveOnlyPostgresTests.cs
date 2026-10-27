using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;
using Microsoft.EntityFrameworkCore;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>Real PostgreSQL evidence that unsupported legacy tables produce a
/// clearly limited, tenant-scoped downloadable archive, never a restore point.</summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantArchiveOnlyPostgresTests(PostgresFixture db)
{
    private sealed class TestUser : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "test-head";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => null;
    }

    [SkippableFact]
    public async Task RetainedPhysicalTable_AllowsStoredArchive_BlocksRestore_AndPreservesTenantIsolation()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "PostgreSQL fixture unavailable");
        await db.ResetAsync();

        var first = Municipality.Create("ARCHA" + Guid.NewGuid().ToString("N")[..7],
            "Archive A", "Province", MunicipalityStatus.Active,
            tenantCode: "archive-a-" + Guid.NewGuid().ToString("N")[..10]);
        var second = Municipality.Create("ARCHB" + Guid.NewGuid().ToString("N")[..7],
            "Archive B", "Province", MunicipalityStatus.Active,
            tenantCode: "archive-b-" + Guid.NewGuid().ToString("N")[..10]);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.AddRange(first, second);
            await setup.SaveChangesAsync();
        }
        await using (var ctx = db.CreateContext(first.Id))
        {
            ctx.Facilities.Add(Facility.Create(FacilityCode.NPM, "Archive first market", "NPM"));
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = db.CreateContext(second.Id))
        {
            ctx.Facilities.Add(Facility.Create(FacilityCode.NPM, "Archive second market", "NPM"));
            await ctx.SaveChangesAsync();
        }

        const string table = "StallTrackLegacyArchiveProof";
        try
        {
            await using (var schema = db.CreateContext(Guid.Empty))
                await schema.Database.ExecuteSqlRawAsync(
                    """CREATE TABLE public."StallTrackLegacyArchiveProof" ("Id" uuid primary key, "MunicipalityId" uuid not null, "Amount" numeric(12,2) not null)""");
            await using var context = db.CreateContext(first.Id);
            var restore = new TenantRestoreRepository(context, new TestUser());
            // This is a deliberate refusal: no restore-ready backup may ignore a physical legacy table.
            await Assert.ThrowsAsync<InvalidOperationException>(() => restore.CreateSnapshotAsync(default));

            var archive = await restore.CreateStoredBackupSnapshotAsync(default);
            Assert.Equal(TenantRestoreSnapshot.ArchiveOnlyFormatVersion, archive.FormatVersion);
            Assert.Contains(table, archive.ExcludedLegacyTables!);
            Assert.False(archive.Tables.ContainsKey(table));
            Assert.Contains("Archive first market", archive.Tables["Facilities"]);
            Assert.DoesNotContain("Archive second market", archive.Tables["Facilities"]);
            Assert.Equal(first.Id, archive.MunicipalityId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => restore.RestoreAsync(archive, default));

            var user = new TestUser();
            var repo = new TenantBackupRepository(context, restore, user);
            var info = await repo.CreateAsync("Panel demo", default);
            Assert.False(info.IsRestoreReady);
            var file = await repo.GetFileAsync(info.Id, default);
            Assert.NotNull(file);
            Assert.False(file.Value.Info.IsRestoreReady);
            var stored = JsonSerializer.Deserialize<TenantRestoreSnapshot>(file.Value.Bytes);
            Assert.NotNull(stored);
            Assert.Equal(TenantRestoreSnapshot.ArchiveOnlyFormatVersion, stored!.FormatVersion);
            var contents = await repo.GetContentsAsync(info.Id, default);
            Assert.NotNull(contents);
            Assert.False(contents!.IsRestoreReady);
            Assert.True(contents.RowCount > 0);
            await using var otherTenant = db.CreateContext(second.Id);
            var theirBackups = await new TenantBackupRepository(otherTenant,
                new TenantRestoreRepository(otherTenant, new TestUser()),
                user).ListAsync(default);
            Assert.DoesNotContain(theirBackups, b => b.Id == info.Id);
        }
        finally
        {
            await using var clean = db.CreateContext(Guid.Empty);
            await clean.Database.ExecuteSqlRawAsync(
                """DROP TABLE IF EXISTS public."StallTrackLegacyArchiveProof" """);
        }
    }
}
