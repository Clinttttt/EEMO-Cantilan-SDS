using EEMOCantilanSDS.Application.Command.Payments.ClearMonthlyException;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>Clearing a monthly exception removes it when present and is a no-op (still success) otherwise.</summary>
public class ClearStallMonthlyExceptionCommandHandlerTests
{
    [Fact]
    public async Task WhenExists_RemovesException()
    {
        var existing = StallMonthlyException.Create(Guid.NewGuid(), 2026, 6, MonthlyExceptionReason.ApprovedByEemo);
        var repo = new Mock<IStallMonthlyExceptionRepository>();
        var uow = new Mock<IUnitOfWork>();
        repo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new ClearStallMonthlyExceptionCommandHandler(repo.Object, uow.Object);
        var result = await handler.Handle(new ClearStallMonthlyExceptionCommand(existing.StallId, 2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.Remove(existing), Times.Once);
    }

    [Fact]
    public async Task WhenNone_IsIdempotent()
    {
        var repo = new Mock<IStallMonthlyExceptionRepository>();
        var uow = new Mock<IUnitOfWork>();
        repo.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StallMonthlyException?)null);

        var handler = new ClearStallMonthlyExceptionCommandHandler(repo.Object, uow.Object);
        var result = await handler.Handle(new ClearStallMonthlyExceptionCommand(Guid.NewGuid(), 2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.Remove(It.IsAny<StallMonthlyException>()), Times.Never);
    }
}
