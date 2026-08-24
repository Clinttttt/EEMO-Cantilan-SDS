using EEMOCantilanSDS.Application.Command.Backup.RestoreTenantFromBackup;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Restore;

/// <summary>
/// Restoring a backup clears the office's cached views, and only on a restore that actually happened.
///
/// Reported from use on 2026-08-24: a vendor added to the barbecue stand and then removed by a restore still counted on
/// that facility's page until the page was reloaded. The restore itself was right; it simply told no cache. This pins the
/// wiring — the rule itself is pinned in TenantCacheInvalidationTests — because a purge that resolves correctly and is
/// called by nobody clears nothing.
/// </summary>
public class RestoreTenantFromBackupCacheTests
{
    private sealed class SpyInvalidator : IEemoCacheInvalidator
    {
        public List<string> TenantsPurged { get; } = new();

        public Task InvalidateRegionAsync(string region, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string tenantCode, FacilityCode facilityCode, int year, int month, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string tenantCode, FacilityCode? facilityCode, int year, int month, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken ct = default) => Task.CompletedTask;

        public Task InvalidateTenantAsync(string tenantCode, CancellationToken ct = default)
        {
            TenantsPurged.Add(tenantCode);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTenant(string code) : ITenantContext
    {
        public string TenantCode => code;
        public Guid? MunicipalityId => Guid.NewGuid();
    }

    private static (RestoreTenantFromBackupCommandHandler Handler, SpyInvalidator Spy) Build(
        bool snapshotFound = true, string role = "SuperAdmin")
    {
        var admin = AdminUser.Create(
            "Head", "head", "head@lgu.gov.ph", TestPasswords.Hash("HeadPass123!"),
            AdminRole.SuperAdmin, Guid.NewGuid(), isActive: true);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("head");
        currentUser.SetupGet(c => c.Role).Returns(role);

        var auth = new Mock<IAuthRepository>();
        auth.Setup(a => a.GetAdminByUsernameAsync("head", It.IsAny<CancellationToken>())).ReturnsAsync(admin);

        var backups = new Mock<ITenantBackupRepository>();
        backups.Setup(b => b.GetSnapshotAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshotFound ? new TenantRestoreSnapshot(TenantRestoreSnapshot.CurrentFormatVersion, "madrid", Guid.NewGuid(), DateTime.UtcNow, new Dictionary<string, string>()) : null);

        var restore = new Mock<ITenantRestoreRepository>();
        restore.Setup(r => r.RestoreAsync(It.IsAny<TenantRestoreSnapshot>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantRestoreResult(3, 12, new Dictionary<string, int> { ["Stalls"] = 12 }));

        var spy = new SpyInvalidator();

        return (new RestoreTenantFromBackupCommandHandler(
            currentUser.Object, auth.Object, backups.Object, restore.Object,
            NullLogger<RestoreTenantFromBackupCommandHandler>.Instance, new IdentityPasswordHasher(),
            spy, new FixedTenant("madrid")), spy);
    }

    [Fact]
    public async Task ASuccessfulRestorePurgesThatOfficesCache()
    {
        var (handler, spy) = Build();

        var result = await handler.Handle(
            new RestoreTenantFromBackupCommand(Guid.NewGuid(), "RESTORE", "HeadPass123!"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "madrid" }, spy.TenantsPurged);
    }

    [Fact]
    public async Task ARefusedRestorePurgesNothing()
    {
        // Nothing was written, so nothing cached is wrong. Clearing anyway would throw away every office's warm views on
        // a mistyped confirmation.
        var (handler, spy) = Build();

        var wrongPhrase = await handler.Handle(
            new RestoreTenantFromBackupCommand(Guid.NewGuid(), "restore please", "HeadPass123!"), default);
        var wrongPassword = await handler.Handle(
            new RestoreTenantFromBackupCommand(Guid.NewGuid(), "RESTORE", "not-the-password"), default);

        Assert.False(wrongPhrase.IsSuccess);
        Assert.False(wrongPassword.IsSuccess);
        Assert.Empty(spy.TenantsPurged);
    }

    [Fact]
    public async Task AMissingBackupPurgesNothing()
    {
        var (handler, spy) = Build(snapshotFound: false);

        var result = await handler.Handle(
            new RestoreTenantFromBackupCommand(Guid.NewGuid(), "RESTORE", "HeadPass123!"), default);

        Assert.False(result.IsSuccess);
        Assert.Empty(spy.TenantsPurged);
    }
}
