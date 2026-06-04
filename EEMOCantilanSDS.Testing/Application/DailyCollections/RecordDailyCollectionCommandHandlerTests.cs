using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class RecordDailyCollectionCommandHandlerTests
{
    // Regression: previously the handler hardcoded collectorId: Guid.Empty / createdBy: "System",
    // so collector daily-collection stats were structurally always zero.
    [Fact]
    public async Task Handle_AttributesCollectionToAuthenticatedUser()
    {
        var collectorId = Guid.NewGuid();
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        currentUser.SetupGet(c => c.Username).Returns("collector1");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        DailyCollection? captured = null;
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured = dc)
            .Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, stallRepo.Object, currentUser.Object, uow.Object);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, DateOnly.FromDateTime(DateTime.UtcNow), IsPaid: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.True(captured!.IsPaid);
        Assert.Equal(collectorId, captured.CollectorId);
        Assert.Equal("collector1", captured.CreatedBy);
    }
}
