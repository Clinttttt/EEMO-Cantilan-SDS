using EEMOCantilanSDS.Application.Command.Notifications.RegisterDeviceToken;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class RegisterDeviceTokenCommandHandlerTests
{
    private static (RegisterDeviceTokenCommandHandler handler, Mock<ICollectorDeviceTokenRepository> repo) Build(Guid? collectorId, Guid? municipalityId = null)
    {
        var repo = new Mock<ICollectorDeviceTokenRepository>();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.CollectorId).Returns(collectorId);
        user.SetupGet(u => u.MunicipalityId).Returns(municipalityId);
        return (new RegisterDeviceTokenCommandHandler(repo.Object, user.Object), repo);
    }

    [Fact]
    public async Task NoCollector_IsForbidden()
    {
        var (handler, repo) = Build(collectorId: null);

        var result = await handler.Handle(new RegisterDeviceTokenCommand("tok", "android"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        repo.Verify(r => r.UpsertAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmptyToken_Fails_WithoutUpsert()
    {
        var (handler, repo) = Build(collectorId: Guid.NewGuid());

        var result = await handler.Handle(new RegisterDeviceTokenCommand("  ", "android"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        repo.Verify(r => r.UpsertAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidCollector_UpsertsTokenUnderThatCollector()
    {
        var collectorId = Guid.NewGuid();
        var municipalityId = Guid.NewGuid();
        var (handler, repo) = Build(collectorId, municipalityId);

        var result = await handler.Handle(new RegisterDeviceTokenCommand("fcm-token-123", "android"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.UpsertAsync(collectorId, "fcm-token-123", "android", municipalityId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
