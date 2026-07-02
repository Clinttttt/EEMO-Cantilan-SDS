using EEMOCantilanSDS.Application.Command.DailyCollections.SaveDailyCollectionOrNumber;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Payments;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Adding an OR number to an NPM daily collection: only an existing PAID day can be receipted, and
/// the OR is stamped without disturbing the paid amount/collector/fish.
/// </summary>
public class SaveDailyCollectionOrNumberCommandHandlerTests
{
    private static readonly Guid Stall = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 6, 24);

    private static (SaveDailyCollectionOrNumberCommandHandler handler, Mock<IUnitOfWork> uow) Build(DailyCollection? collection)
    {
        var repo = new Mock<IDailyCollectionRepository>();
        repo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(c => c.Username).Returns("admin");
        var uow = new Mock<IUnitOfWork>();
        return (new SaveDailyCollectionOrNumberCommandHandler(repo.Object, user.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant), uow);
    }

    [Fact]
    public async Task SetsOrNumber_OnPaidDay()
    {
        var collection = DailyCollection.Create(Stall, Day);
        collection.MarkPaid(string.Empty, collectorId: null);   // paid in the field with no OR yet
        var (handler, uow) = Build(collection);

        var result = await handler.Handle(
            new SaveDailyCollectionOrNumberCommand(Stall, Day, "OR-12345"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("OR-12345", collection.ORNumber);
        Assert.True(collection.IsPaid);   // unchanged
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReturnsNotFound_WhenNoCollectionForThatDay()
    {
        var (handler, uow) = Build(null);

        var result = await handler.Handle(
            new SaveDailyCollectionOrNumberCommand(Stall, Day, "OR-1"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReturnsNotFound_WhenDayIsUnpaid()
    {
        var collection = DailyCollection.Create(Stall, Day);   // created but not paid
        var (handler, uow) = Build(collection);

        var result = await handler.Handle(
            new SaveDailyCollectionOrNumberCommand(Stall, Day, "OR-1"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(collection.ORNumber);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
