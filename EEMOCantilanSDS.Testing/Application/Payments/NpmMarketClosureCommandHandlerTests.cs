using EEMOCantilanSDS.Application.Command.Payments.ClearMarketClosure;
using EEMOCantilanSDS.Application.Command.Payments.SetMarketClosure;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Market-closure set is an upsert (no duplicate per date); clear is idempotent.
/// </summary>
public class NpmMarketClosureCommandHandlerTests
{
    [Fact]
    public async Task Set_WhenNoneExists_CreatesClosure()
    {
        var repo = new Mock<INpmMarketClosureRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        repo.Setup(r => r.GetAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((NpmMarketClosure?)null);
        currentUser.SetupGet(c => c.Username).Returns("tester");

        var handler = new SetNpmMarketClosureCommandHandler(repo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);
        var result = await handler.Handle(
            new SetNpmMarketClosureCommand(new DateOnly(2026, 6, 15), MarketClosureReason.Holiday), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddAsync(It.IsAny<NpmMarketClosure>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_WhenExists_UpdatesInPlace_NoDuplicate()
    {
        var existing = NpmMarketClosure.Create(new DateOnly(2026, 6, 15));
        var repo = new Mock<INpmMarketClosureRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        repo.Setup(r => r.GetAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        currentUser.SetupGet(c => c.Username).Returns("tester");

        var handler = new SetNpmMarketClosureCommandHandler(repo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);
        var result = await handler.Handle(
            new SetNpmMarketClosureCommand(new DateOnly(2026, 6, 15), MarketClosureReason.Weather, "Typhoon"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddAsync(It.IsAny<NpmMarketClosure>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(MarketClosureReason.Weather, existing.Reason);
        Assert.Equal("Typhoon", existing.Remarks);
    }

    [Fact]
    public async Task Clear_WhenExists_RemovesClosure()
    {
        var existing = NpmMarketClosure.Create(new DateOnly(2026, 6, 15));
        var repo = new Mock<INpmMarketClosureRepository>();
        var uow = new Mock<IUnitOfWork>();
        repo.Setup(r => r.GetAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var handler = new ClearNpmMarketClosureCommandHandler(repo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);
        var result = await handler.Handle(new ClearNpmMarketClosureCommand(new DateOnly(2026, 6, 15)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.Remove(existing), Times.Once);
    }

    [Fact]
    public async Task Clear_WhenNone_IsIdempotent()
    {
        var repo = new Mock<INpmMarketClosureRepository>();
        var uow = new Mock<IUnitOfWork>();
        repo.Setup(r => r.GetAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((NpmMarketClosure?)null);

        var handler = new ClearNpmMarketClosureCommandHandler(repo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);
        var result = await handler.Handle(new ClearNpmMarketClosureCommand(new DateOnly(2026, 6, 15)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.Remove(It.IsAny<NpmMarketClosure>()), Times.Never);
    }
}
