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
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

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
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, DateOnly.FromDateTime(DateTime.UtcNow), IsPaid: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.True(captured!.IsPaid);
        Assert.Equal(collectorId, captured.CollectorId);
        Assert.Equal("collector1", captured.CreatedBy);
    }

    [Fact]
    public async Task Handle_PersistsOrNumberForPaidDailyCollection()
    {
        var collectorId = Guid.NewGuid();
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

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
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), true, ORNumber: "000001"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.True(captured!.IsPaid);
        Assert.Equal("000001", captured.ORNumber);
    }

    [Fact]
    public async Task Handle_CreatesUnpaidMarkerForSameDayRefresh()
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        currentUser.SetupGet(c => c.Username).Returns("collector1");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        DailyCollection? captured = null;
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured = dc)
            .Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.False(captured!.IsPaid);
        Assert.Equal(new DateOnly(2026, 6, 6), captured.CollectionDate);
    }

    [Fact]
    public async Task Handle_NewPaidCollection_WithDuplicateOrNumber_ReturnsConflict()
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        currentUser.SetupGet(c => c.Username).Returns("collector1");

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), true, ORNumber: "DUP-1"),
            CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        dailyRepo.Verify(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingPaidCollection_ResavingSameOrNumber_IsAllowed()
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);
        var existing = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 6), "prev");
        existing.MarkPaid(orNumber: "X-1", collectorId: null, fishKilos: null, updatedBy: "prev");

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        // OR "exists" globally only because it is on this same day's record.
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        currentUser.SetupGet(c => c.Username).Returns("collector1");

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), true, ORNumber: "X-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existing.IsPaid);
        Assert.Equal("X-1", existing.ORNumber);
    }
}
