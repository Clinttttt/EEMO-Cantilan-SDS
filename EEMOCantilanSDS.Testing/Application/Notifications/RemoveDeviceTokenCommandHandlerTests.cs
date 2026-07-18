using EEMOCantilanSDS.Application.Command.Notifications.RemoveDeviceToken;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class RemoveDeviceTokenCommandHandlerTests
{
    private static (RemoveDeviceTokenCommandHandler handler, Mock<ICollectorDeviceTokenRepository> repo) Build(Guid? collectorId)
    {
        var repo = new Mock<ICollectorDeviceTokenRepository>();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.CollectorId).Returns(collectorId);
        return (new RemoveDeviceTokenCommandHandler(repo.Object, user.Object), repo);
    }

    [Fact]
    public async Task NoCollector_IsForbidden()
    {
        var (handler, repo) = Build(collectorId: null);

        var result = await handler.Handle(new RemoveDeviceTokenCommand("tok"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        repo.Verify(r => r.RemoveByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyToken_IsNoOpSuccess()
    {
        var (handler, repo) = Build(collectorId: Guid.NewGuid());

        var result = await handler.Handle(new RemoveDeviceTokenCommand("  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.RemoveByTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidCollector_RemovesToken()
    {
        var (handler, repo) = Build(collectorId: Guid.NewGuid());

        var result = await handler.Handle(new RemoveDeviceTokenCommand("fcm-token-123"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.RemoveByTokenAsync("fcm-token-123", It.IsAny<CancellationToken>()), Times.Once);
    }
}
