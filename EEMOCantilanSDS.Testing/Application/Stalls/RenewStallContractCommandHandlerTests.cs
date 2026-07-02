using EEMOCantilanSDS.Application.Command.Stalls.RenewStallContract;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Renewing an expired account terminates the current term (kept as history) and starts a fresh term
/// at the stall's current rate. The lapsed gap has no active contract, so it is never back-billed.
/// </summary>
public class RenewStallContractCommandHandlerTests
{
    [Fact]
    public async Task Renew_TerminatesOldTerm_AndAddsNewTermAtStallRate()
    {
        var stall = Stall.Create(Guid.NewGuid(), "5", 1500m, ApplicableFees.BaseRental);
        var oldTerm = Contract.Create(stall.Id, "Old Occupant", "Old Name", new DateOnly(2024, 1, 1), 1, 1500m);
        stall.Contracts.Add(oldTerm);

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdWithContractsAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        Contract? added = null;
        stallRepo.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()))
            .Callback<Contract, CancellationToken>((c, _) => added = c)
            .Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("tester");
        var uow = new Mock<IUnitOfWork>();

        var handler = new RenewStallContractCommandHandler(stallRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        var newStart = new DateOnly(2026, 6, 28);
        var result = await handler.Handle(
            new RenewStallContractCommand(stall.Id, newStart, 3, "New Occupant", "New Name"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(oldTerm.IsActive);                 // old term terminated (kept as history)
        Assert.NotNull(added);
        Assert.True(added!.IsActive);
        Assert.Equal(newStart, added.EffectivityDate);
        Assert.Equal(3, added.DurationYears);
        Assert.Equal("New Occupant", added.ActualOccupant);
        Assert.Equal(1500m, added.MonthlyRentalRate);   // keeps the stall's current rate
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Renew_MissingStall_ReturnsNotFound()
    {
        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdWithContractsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stall?)null);
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();

        var handler = new RenewStallContractCommandHandler(stallRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);
        var result = await handler.Handle(
            new RenewStallContractCommand(Guid.NewGuid(), new DateOnly(2026, 6, 28), 3, "X", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
