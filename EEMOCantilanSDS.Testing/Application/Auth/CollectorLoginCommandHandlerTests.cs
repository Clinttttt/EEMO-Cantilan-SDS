using EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class CollectorLoginCommandHandlerTests
{
    private const string Password = "Secret123!";

    private static CollectorUser NewCollector() =>
        CollectorUser.Create("Juan Collector", "EEMO-2026-001", "juan", "juan@eemo.gov", "09170000000", Password);

    private static (CollectorLoginCommandHandler handler, Mock<ITokenService> token, Mock<IUnitOfWork> uow) Build(CollectorUser? collector)
    {
        var repo = new Mock<ICollectorRepository>();
        var token = new Mock<ITokenService>();
        var uow = new Mock<IUnitOfWork>();

        repo.Setup(r => r.GetByUsernameOrEmployeeIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collector);
        token.Setup(t => t.CreateTokenResponse(It.IsAny<CollectorUser>()))
            .ReturnsAsync(new TokenResponseDto { AccessToken = "collector-at", RefreshToken = "collector-rt" });

        return (new CollectorLoginCommandHandler(repo.Object, token.Object, uow.Object), token, uow);
    }

    [Fact]
    public async Task ValidActiveCollector_ReturnsToken()
    {
        var (handler, _, _) = Build(NewCollector());

        var result = await handler.Handle(new CollectorLoginCommand("juan", Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("collector-at", result.Value!.AccessToken);
    }

    [Fact]
    public async Task BadPassword_RecordsFailedAttempt_AndReturnsUnauthorized()
    {
        var collector = NewCollector();
        var (handler, token, uow) = Build(collector);

        var result = await handler.Handle(new CollectorLoginCommand("juan", "wrong"), CancellationToken.None);

        Assert.Equal(401, result.StatusCode);
        Assert.Equal(1, collector.FailedAttempts);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<CollectorUser>()), Times.Never);
    }

    [Fact]
    public async Task InactiveCollector_ReturnsForbidden_WithoutIssuingToken()
    {
        var collector = NewCollector();
        collector.Deactivate("test");
        var (handler, token, _) = Build(collector);

        var result = await handler.Handle(new CollectorLoginCommand("juan", Password), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<CollectorUser>()), Times.Never);
    }
}
