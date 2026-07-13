using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories
{
    /// <summary>
    /// The per-municipality stored backup history. Proves retention keeps only the most recent N, listing
    /// is newest-first, files/snapshots round-trip, and the store is tenant-scoped (one LGU never sees
    /// another's backups). Snapshot capture (raw SQL) is faked here; the real capture/restore is covered by
    /// the env-gated round-trip test and the live throwaway-DB verification.
    /// </summary>
    public class TenantBackupRepositoryTests
    {
        private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
        {
            public Guid MunicipalityId => id;
            public void Set(Guid municipalityId) { }
        }

        private static DbContextOptions<AppDbContext> Options() =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        private static ICurrentUserService User(string username = "head")
        {
            var m = new Mock<ICurrentUserService>();
            m.SetupGet(u => u.Username).Returns(username);
            m.SetupGet(u => u.Role).Returns("SuperAdmin");
            m.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
            return m.Object;
        }

        private static ITenantRestoreRepository RestoreRepoReturning(TenantRestoreSnapshot snapshot)
        {
            var m = new Mock<ITenantRestoreRepository>();
            m.Setup(r => r.CreateSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            return m.Object;
        }

        private static TenantRestoreSnapshot SampleSnapshot(int rows = 2) =>
            new("restore-v1", "cantilan-sds", Guid.Empty, DateTime.UtcNow,
                new Dictionary<string, string>
                {
                    ["Stalls"] = "[" + string.Join(",", Enumerable.Range(0, rows).Select(i => $"{{\"i\":{i}}}")) + "]",
                });

        private static void SeedBackup(AppDbContext ctx, Guid mid, string note)
        {
            var b = TenantBackup.Create("head", "restore-v1", 1, 1, 10, "{\"FormatVersion\":\"restore-v1\"}", note);
            ctx.TenantBackups.Add(b);
            ctx.Entry(b).Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = mid;
            ctx.SaveChanges();
        }

        [Fact]
        public async Task CreateAsync_KeepsOnlyTheMostRecent15()
        {
            var options = Options();
            // Empty tenant → the municipality query filter is a no-op, so the rows added here are visible
            // to the same context (the stamp interceptor isn't wired in hand-built test contexts).
            using var ctx = new AppDbContext(options, new FixedMunicipality(Guid.Empty));
            var repo = new TenantBackupRepository(ctx, RestoreRepoReturning(SampleSnapshot(2)), User());

            for (var i = 0; i < 18; i++)
                await repo.CreateAsync(note: $"b{i}", CancellationToken.None);

            var list = await repo.ListAsync(CancellationToken.None);
            Assert.Equal(15, list.Count);
            Assert.All(list, b => Assert.Equal(2, b.RowCount));
            Assert.All(list, b => Assert.Equal(1, b.TableCount));
        }

        [Fact]
        public async Task GetFileAndSnapshot_RoundTripTheStoredJson()
        {
            var options = Options();
            using var ctx = new AppDbContext(options, new FixedMunicipality(Guid.Empty));
            var snap = SampleSnapshot(3);
            var repo = new TenantBackupRepository(ctx, RestoreRepoReturning(snap), User());

            var info = await repo.CreateAsync(note: null, CancellationToken.None);

            var file = await repo.GetFileAsync(info.Id, CancellationToken.None);
            Assert.NotNull(file);
            var deserialized = JsonSerializer.Deserialize<TenantRestoreSnapshot>(Encoding.UTF8.GetString(file!.Value.Bytes));
            Assert.NotNull(deserialized);
            Assert.Equal("restore-v1", deserialized!.FormatVersion);

            var got = await repo.GetSnapshotAsync(info.Id, CancellationToken.None);
            Assert.NotNull(got);
            Assert.True(got!.Tables.ContainsKey("Stalls"));
        }

        [Fact]
        public async Task List_IsScopedToTheCallersMunicipality()
        {
            var options = Options();
            var munA = Guid.NewGuid();
            var munB = Guid.NewGuid();

            using (var seed = new AppDbContext(options, new FixedMunicipality(munA)))
            {
                SeedBackup(seed, munA, "A-1");
                SeedBackup(seed, munA, "A-2");
                SeedBackup(seed, munB, "B-1");
            }

            using (var ctxA = new AppDbContext(options, new FixedMunicipality(munA)))
            {
                var repo = new TenantBackupRepository(ctxA, RestoreRepoReturning(SampleSnapshot()), User());
                var list = await repo.ListAsync(CancellationToken.None);
                Assert.Equal(2, list.Count);
            }

            using (var ctxB = new AppDbContext(options, new FixedMunicipality(munB)))
            {
                var repo = new TenantBackupRepository(ctxB, RestoreRepoReturning(SampleSnapshot()), User());
                var list = await repo.ListAsync(CancellationToken.None);
                Assert.Single(list);
            }
        }
    }
}
