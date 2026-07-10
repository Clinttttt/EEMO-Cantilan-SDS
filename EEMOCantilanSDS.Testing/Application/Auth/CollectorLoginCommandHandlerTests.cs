using EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class CollectorLoginCommandHandlerTests
{
    private const string Password = "Secret123!";

    private static CollectorUser NewCollector(Guid municipalityId = default) =>
        CollectorUser.Create("Juan Collector", "EEMO-2026-001", "juan", "juan@eemo.gov", "09170000000", Password, municipalityId);

    private static (CollectorLoginCommandHandler handler, Mock<ITokenService> token, Mock<IUnitOfWork> uow, Mock<IMunicipalityRepository> muni) Build(CollectorUser? collector)
    {
        var repo = new Mock<ICollectorRepository>();
        var muni = new Mock<IMunicipalityRepository>();
        var token = new Mock<ITokenService>();
        var uow = new Mock<IUnitOfWork>();

        repo.Setup(r => r.GetByUsernameOrEmployeeIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collector);
        // Scoped login (?lgu={code}) resolves the tenant first and uses the tenant-scoped overload; stub it
        // to the same account so the per-municipality boundary check is what these tests exercise.
        repo.Setup(r => r.GetByUsernameOrEmployeeIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collector);
        token.Setup(t => t.CreateTokenResponse(It.IsAny<CollectorUser>()))
            .ReturnsAsync(new TokenResponseDto { AccessToken = "collector-at", RefreshToken = "collector-rt" });

        return (new CollectorLoginCommandHandler(repo.Object, muni.Object, token.Object, uow.Object), token, uow, muni);
    }

    [Fact]
    public async Task ValidActiveCollector_ReturnsToken()
    {
        var (handler, _, _, _) = Build(NewCollector());

        // No municipality code → today's behaviour, global lookup, no boundary.
        var result = await handler.Handle(new CollectorLoginCommand("juan", Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("collector-at", result.Value!.AccessToken);
    }

    [Fact]
    public async Task BadPassword_RecordsFailedAttempt_AndReturnsUnauthorized()
    {
        var collector = NewCollector();
        var (handler, token, uow, _) = Build(collector);

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
        var (handler, token, _, _) = Build(collector);

        var result = await handler.Handle(new CollectorLoginCommand("juan", Password), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<CollectorUser>()), Times.Never);
    }

    // ── Per-municipality login boundary (scoped login ?lgu={code}) ──────────────────────────────────

    [Fact]
    public async Task ScopedLogin_AccountBelongsToThatMunicipality_ReturnsToken()
    {
        var lgu = Municipality.Create("CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active);
        var collector = NewCollector(lgu.Id);
        var (handler, token, _, muni) = Build(collector);
        muni.Setup(m => m.GetByIdentifierAsync("cantilan", It.IsAny<CancellationToken>())).ReturnsAsync(lgu);

        var result = await handler.Handle(new CollectorLoginCommand("juan", Password, "cantilan"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<CollectorUser>()), Times.Once);
    }

    [Fact]
    public async Task ScopedLogin_AccountBelongsToAnotherMunicipality_ReturnsForbidden_WithoutToken()
    {
        // The login is scoped to Carrascal, but this (correctly-authenticated) collector belongs elsewhere.
        var carrascal = Municipality.Create("CARRASCAL", "Carrascal", "Surigao del Sur", MunicipalityStatus.Active);
        var collector = NewCollector(Guid.NewGuid());   // a different municipality
        var (handler, token, _, muni) = Build(collector);
        muni.Setup(m => m.GetByIdentifierAsync("carrascal", It.IsAny<CancellationToken>())).ReturnsAsync(carrascal);

        var result = await handler.Handle(new CollectorLoginCommand("juan", Password, "carrascal"), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<CollectorUser>()), Times.Never);
    }

    [Fact]
    public async Task ScopedLogin_UnknownMunicipalityCode_ReturnsForbidden_WithoutToken()
    {
        var (handler, token, _, muni) = Build(NewCollector());
        muni.Setup(m => m.GetByIdentifierAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Municipality?)null);

        var result = await handler.Handle(new CollectorLoginCommand("juan", Password, "does-not-exist"), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        token.Verify(t => t.CreateTokenResponse(It.IsAny<CollectorUser>()), Times.Never);
    }
}
