using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Audit;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

public sealed class AuditDetailEnhancementTests
{
    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid value) { }
    }

    [Fact]
    public async Task CollectorLogin_IsHousekeeping_ButRealAccountChangeRemainsAudited()
    {
        var user = new Mock<ICurrentUserService>();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(user.Object))
            .Options;
        using var db = new AppDbContext(options);
        var collector = CollectorUser.Create("Test Collector", "C001", "collector", null, null,
            new HashedPassword("test-hash"));
        db.CollectorUsers.Add(collector);
        await db.SaveChangesAsync();
        var original = await db.AuditLogs.CountAsync();

        collector.RecordLogin();
        await db.SaveChangesAsync();
        Assert.Equal(original, await db.AuditLogs.CountAsync());

        collector.UpdateProfile("New Collector Name", null, null, "head");
        await db.SaveChangesAsync();
        Assert.Equal(original + 1, await db.AuditLogs.CountAsync());
        Assert.Contains(await db.AuditLogs.ToListAsync(),
            x => x.EntityType == "CollectorUser" && x.Action == "Updated");
    }

    [Fact]
    public async Task PaymentGatewayPayloadAndCheckoutUrl_AreNotPersistedInNewAuditSnapshots()
    {
        var user = new Mock<ICurrentUserService>();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(user.Object))
            .Options;
        using var db = new AppDbContext(options);
        var payment = OnlinePaymentTransaction.Create("ST-01", Guid.NewGuid(), Guid.NewGuid(), 950m, "PayMongo");
        db.Add(payment);
        await db.SaveChangesAsync();

        payment.SetPending("session-example", "https://checkout.example/?secret=NEVER_STORE_ME");
        await db.SaveChangesAsync();
        payment.MarkFailed("{\"authorization\":\"ALSO_NEVER_STORE_ME\"}");
        await db.SaveChangesAsync();

        var auditJson = string.Join(" ", await db.AuditLogs
            .Where(x => x.EntityType == "OnlinePaymentTransaction")
            .Select(x => (x.OldValues ?? "") + (x.NewValues ?? "")).ToListAsync());
        Assert.DoesNotContain("NEVER_STORE_ME", auditJson);
        Assert.DoesNotContain("ALSO_NEVER_STORE_ME", auditJson);
        Assert.Contains("[redacted]", auditJson);
        Assert.Contains("GatewayReference", auditJson);
    }

    [Fact]
    public void DetailComposer_DisplaysAllReviewedChanges_ButNeverRawPayload()
    {
        var oldValues = "{\"AmountPaid\":10,\"BalanceDue\":90,\"Status\":1,\"ORNumber\":\"A\",\"MonthlyRate\":100,\"DailyRate\":10,\"RawPayload\":\"PRIVATE\"}";
        var newValues = "{\"AmountPaid\":20,\"BalanceDue\":80,\"Status\":3,\"ORNumber\":\"B\",\"MonthlyRate\":200,\"DailyRate\":20,\"RawPayload\":\"LEAK\"}";
        var preview = AuditDetailComposer.Changes(oldValues, newValues);
        var all = AuditDetailComposer.ChangesAll(oldValues, newValues);
        Assert.Equal(4, preview.Count);
        Assert.Equal(6, all.Count);
        Assert.DoesNotContain(all, s => s.Contains("PRIVATE") || s.Contains("LEAK"));
    }

    [Fact]
    public async Task SingleEvent_RespectsMunicipalityScope_AndNeverReturnsSnapshots()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var eventA = AuditLog.Create("system", "system", "system", "Updated", "OnlinePaymentTransaction",
            Guid.NewGuid(), "{\"RawPayload\":\"PRIVATE\"}", "{\"RawPayload\":\"LEAK\"}",
            municipalityId: tenantA);
        var eventB = AuditLog.Create("system", "system", "system", "Created", "PaymentRecord",
            Guid.NewGuid(), municipalityId: tenantB);
        using (var seed = new AppDbContext(options, new FixedMunicipality(tenantA)))
        {
            seed.AuditLogs.AddRange(eventA, eventB);
            await seed.SaveChangesAsync();
        }

        using var db = new AppDbContext(options, new FixedMunicipality(tenantA));
        var repository = new AuditRepository(db, new FixedMunicipality(tenantA));
        var result = await repository.GetAuditEventAsync(eventA.Id, default);
        Assert.NotNull(result);
        Assert.Equal(eventA.Id, result.Id);
        Assert.Empty(result.Changes);
        Assert.DoesNotContain("PRIVATE", result.Details);
        Assert.Null(await repository.GetAuditEventAsync(eventB.Id, default));
        Assert.Null(await repository.GetAuditEventAsync(Guid.NewGuid(), default));
    }
}
