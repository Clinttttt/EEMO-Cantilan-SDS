using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The offline-sync batch handler must be idempotent (skip already-synced operations), dispatch each
/// queued operation through the existing validated command, and classify outcomes as
/// Synced / Rejected (terminal 4xx) / Failed (transient 5xx) per item.
/// </summary>
public class SyncOfflineCollectionsCommandHandlerTests
{
    private static (SyncOfflineCollectionsCommandHandler handler, Mock<ISender> sender, Mock<ISyncRepository> syncRepo) Build(Guid? collectorId)
    {
        var sender = new Mock<ISender>();
        var syncRepo = new Mock<ISyncRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        return (new SyncOfflineCollectionsCommandHandler(sender.Object, syncRepo.Object, currentUser.Object), sender, syncRepo);
    }

    private static SyncOfflineOperationDto NpmOp(Guid id) =>
        new(id, OfflineOperationKind.NpmDaily, new DateOnly(2026, 6, 5), ORNumber: "OR-1", StallId: Guid.NewGuid(), IsPaid: true);

    [Fact]
    public async Task NonCollector_IsForbidden()
    {
        var (handler, _, _) = Build(collectorId: null);

        var result = await handler.Handle(
            new SyncOfflineCollectionsCommand(new[] { NpmOp(Guid.NewGuid()) }), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task AlreadyProcessed_ReturnsSynced_WithoutDispatching()
    {
        var (handler, sender, syncRepo) = Build(Guid.NewGuid());
        syncRepo.Setup(r => r.IsOperationProcessedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await handler.Handle(
            new SyncOfflineCollectionsCommand(new[] { NpmOp(Guid.NewGuid()) }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.SyncedCount);
        Assert.Equal(SyncResultStatus.Synced, result.Value.Results[0].Status);
        sender.Verify(s => s.Send(It.IsAny<RecordDailyCollectionCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NewOperation_Success_IsSynced()
    {
        var (handler, sender, syncRepo) = Build(Guid.NewGuid());
        syncRepo.Setup(r => r.IsOperationProcessedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        sender.Setup(s => s.Send(It.IsAny<RecordDailyCollectionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));

        var result = await handler.Handle(
            new SyncOfflineCollectionsCommand(new[] { NpmOp(Guid.NewGuid()) }), CancellationToken.None);

        Assert.Equal(1, result.Value!.SyncedCount);
        Assert.Equal(SyncResultStatus.Synced, result.Value.Results[0].Status);
        sender.Verify(s => s.Send(It.IsAny<RecordDailyCollectionCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DuplicateOrNumber_IsRejected_NotRetried()
    {
        var (handler, sender, syncRepo) = Build(Guid.NewGuid());
        syncRepo.Setup(r => r.IsOperationProcessedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        sender.Setup(s => s.Send(It.IsAny<RecordDailyCollectionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure("OR number already exists.", 409));

        var result = await handler.Handle(
            new SyncOfflineCollectionsCommand(new[] { NpmOp(Guid.NewGuid()) }), CancellationToken.None);

        Assert.Equal(1, result.Value!.RejectedCount);
        Assert.Equal(SyncResultStatus.Rejected, result.Value.Results[0].Status);
        Assert.Equal("OR number already exists.", result.Value.Results[0].Message);
    }

    [Fact]
    public async Task ServerError_IsFailed_ForRetry()
    {
        var (handler, sender, syncRepo) = Build(Guid.NewGuid());
        syncRepo.Setup(r => r.IsOperationProcessedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        sender.Setup(s => s.Send(It.IsAny<RecordDailyCollectionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Failure("boom", 500));

        var result = await handler.Handle(
            new SyncOfflineCollectionsCommand(new[] { NpmOp(Guid.NewGuid()) }), CancellationToken.None);

        Assert.Equal(1, result.Value!.FailedCount);
        Assert.Equal(SyncResultStatus.Failed, result.Value.Results[0].Status);
    }
}
