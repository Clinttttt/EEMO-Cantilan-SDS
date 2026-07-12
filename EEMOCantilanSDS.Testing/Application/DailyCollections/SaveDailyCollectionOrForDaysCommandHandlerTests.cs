using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrForDays;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Payments;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Option B — applying one Official Receipt to several PAID days of the same stall in a single
/// transaction (one physical receipt covering multiple days).
/// </summary>
public class SaveDailyCollectionOrForDaysCommandHandlerTests
{
    private static readonly Guid Stall = Guid.NewGuid();

    private static SaveDailyCollectionOrForDaysCommandHandler Build(
        IReadOnlyDictionary<DateOnly, DailyCollection?> byDate, out Mock<IUnitOfWork> uow)
    {
        var repo = new Mock<IDailyCollectionRepository>();
        repo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, DateOnly d, CancellationToken _) => byDate.TryGetValue(d, out var c) ? c : null);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(c => c.Username).Returns("admin");
        uow = new Mock<IUnitOfWork>();
        return new SaveDailyCollectionOrForDaysCommandHandler(
            repo.Object, user.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);
    }

    [Fact]
    public async Task AppliesOneOr_ToEveryPaidDay()
    {
        var d1 = new DateOnly(2026, 6, 1);
        var d2 = new DateOnly(2026, 6, 2);
        var c1 = DailyCollection.Create(Stall, d1); c1.MarkPaid(string.Empty, collectorId: null);
        var c2 = DailyCollection.Create(Stall, d2); c2.MarkPaid(string.Empty, collectorId: null);
        var handler = Build(new Dictionary<DateOnly, DailyCollection?> { [d1] = c1, [d2] = c2 }, out var uow);

        var result = await handler.Handle(
            new SaveDailyCollectionOrForDaysCommand(Stall, new[] { d1, d2 }, "OR-777"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("OR-777", c1.ORNumber);
        Assert.Equal("OR-777", c2.ORNumber);   // the SAME OR covers both days
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Fails_WhenNoSelectedDayIsPaid()
    {
        var d1 = new DateOnly(2026, 6, 1);
        var handler = Build(new Dictionary<DateOnly, DailyCollection?> { [d1] = null }, out var uow);

        var result = await handler.Handle(
            new SaveDailyCollectionOrForDaysCommand(Stall, new[] { d1 }, "OR-1"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
