using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class AuditInterceptorTests
{
    [Fact]
    public async Task FinancialMutation_WritesAttributedAuditLog()
    {
        var actorId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(actorId);
        currentUser.SetupGet(c => c.Username).Returns("head");
        currentUser.SetupGet(c => c.Role).Returns("Admin");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(currentUser.Object))
            .Options;

        using var context = new AppDbContext(options);
        var payment = PaymentRecord.Create(Guid.NewGuid(), 2026, 1, 900m);
        context.Add(payment);
        await context.SaveChangesAsync();

        var log = await context.AuditLogs.SingleAsync();
        Assert.Equal("Created", log.Action);
        Assert.Equal(nameof(PaymentRecord), log.EntityType);
        Assert.Equal(payment.Id, log.EntityId);
        Assert.Equal(actorId.ToString(), log.ActorId);
        Assert.Equal("head", log.ActorName);
        Assert.NotNull(log.NewValues);
    }

    private static AppDbContext NewAuditedContext()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.Username).Returns("head");
        currentUser.SetupGet(c => c.Role).Returns("SuperAdmin");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(currentUser.Object))
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task AccountCreation_IsAudited_WithPasswordRedacted()
    {
        using var context = NewAuditedContext();
        var admin = AdminUser.Create("New Admin", "newadmin", "n@a.com", "Secret123!", AdminRole.Admin);
        context.Add(admin);
        await context.SaveChangesAsync();

        var log = await context.AuditLogs.SingleAsync(a => a.EntityType == "AdminUser");
        Assert.Equal("Created", log.Action);
        Assert.Equal(admin.Id, log.EntityId);
        Assert.NotNull(log.NewValues);
        // The password hash must never appear in the audit snapshot.
        Assert.DoesNotContain(admin.PasswordHash, log.NewValues!);
        Assert.Contains("[redacted]", log.NewValues!);
    }

    [Fact]
    public async Task Login_TokenRefresh_IsNotAudited_But_ProfileUpdate_Is()
    {
        using var context = NewAuditedContext();
        var admin = AdminUser.Create("New Admin", "newadmin", "n@a.com", "Secret123!", AdminRole.Admin);
        context.Add(admin);
        await context.SaveChangesAsync();   // 1 audit row: account creation

        // Routine token refresh — only auth-housekeeping columns change → must NOT be audited.
        admin.SetRefreshToken("a-token", DateTime.UtcNow.AddDays(7));
        await context.SaveChangesAsync();
        Assert.Equal(1, await context.AuditLogs.CountAsync(a => a.EntityType == "AdminUser"));

        // A meaningful profile change → audited.
        admin.UpdateProfile("Renamed Admin", "newadmin", "n@a.com", "head");
        await context.SaveChangesAsync();
        Assert.Equal(2, await context.AuditLogs.CountAsync(a => a.EntityType == "AdminUser"));
    }
}
