using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Payments;
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
}
