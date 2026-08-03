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
/// When the stall changes hands (different occupant) the outgoing occupant's payor→stall links are
/// revoked so they can no longer view or pay the incoming occupant's dues; a same-occupant renewal
/// keeps the link intact.
/// </summary>
public class RenewStallContractCommandHandlerTests
{
    private static RenewStallContractCommandHandler Build(
        Mock<IStallRepository> stallRepo, Mock<IPayorRepository> payorRepo, Mock<ICurrentUserService> currentUser, Mock<IUnitOfWork> uow)
        => new(stallRepo.Object, payorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

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

        var payorRepo = new Mock<IPayorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("tester");
        var uow = new Mock<IUnitOfWork>();

        var handler = Build(stallRepo, payorRepo, currentUser, uow);

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
        // Occupant changed (Old → New) → outgoing payor links revoked.
        payorRepo.Verify(p => p.RemoveStallLinksAsync(stall.Id, It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Renew_SameOccupant_KeepsPayorLinks()
    {
        var stall = Stall.Create(Guid.NewGuid(), "5", 1500m, ApplicableFees.BaseRental);
        // Same person, with incidental case/whitespace difference — must still be treated as the same occupant.
        stall.Contracts.Add(Contract.Create(stall.Id, "Maria Santos", null, new DateOnly(2024, 1, 1), 1, 1500m));

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdWithContractsAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        stallRepo.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var payorRepo = new Mock<IPayorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("tester");
        var uow = new Mock<IUnitOfWork>();

        var handler = Build(stallRepo, payorRepo, currentUser, uow);

        var result = await handler.Handle(
            new RenewStallContractCommand(stall.Id, new DateOnly(2026, 6, 28), 3, "  maria santos ", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        payorRepo.Verify(p => p.RemoveStallLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Renew_MissingStall_ReturnsNotFound()
    {
        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdWithContractsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stall?)null);
        var payorRepo = new Mock<IPayorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();

        var handler = Build(stallRepo, payorRepo, currentUser, uow);
        var result = await handler.Handle(
            new RenewStallContractCommand(Guid.NewGuid(), new DateOnly(2026, 6, 28), 3, "X", null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        payorRepo.Verify(p => p.RemoveStallLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The renewal form's Edit view states the whole record the space is re-let on. A corrected rent must reach
    /// both the stall and the new term — otherwise the office would renew at the old rate and the ledger would
    /// bill the old figure.
    /// </summary>
    [Fact]
    public async Task Renew_WithCorrectedRent_LetsTheNewTermAtThatRent()
    {
        var stall = Stall.Create(Guid.NewGuid(), "5", 1500m, ApplicableFees.BaseRental);
        stall.UpdateRates(1500m, dailyRate: 30m);
        stall.Contracts.Add(Contract.Create(stall.Id, "Maria Santos", null, new DateOnly(2024, 1, 1), 1, 1500m));

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdWithContractsAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        Contract? added = null;
        stallRepo.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()))
            .Callback<Contract, CancellationToken>((c, _) => added = c)
            .Returns(Task.CompletedTask);

        var payorRepo = new Mock<IPayorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("tester");
        var uow = new Mock<IUnitOfWork>();

        var handler = Build(stallRepo, payorRepo, currentUser, uow);

        var result = await handler.Handle(
            new RenewStallContractCommand(stall.Id, new DateOnly(2026, 6, 28), 3, "Maria Santos", null,
                MonthlyRate: 1800m, AreaSqm: 6.5, AreaNote: "Corner"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1800m, stall.MonthlyRate);
        Assert.Equal(30m, stall.DailyRate);          // correcting the month must not clear the ordinance daily fee
        Assert.Equal(1800m, added!.MonthlyRentalRate);
        Assert.Equal(6.5, stall.AreaSqm);
        Assert.Equal("Corner", stall.AreaNote);
    }

    /// <summary>
    /// Plain "Proceed" states no figures. Nothing about the space may change then — and in particular a
    /// daily-collected stall must keep its daily rate, which a bare rate update would clear.
    /// </summary>
    [Fact]
    public async Task Renew_WithoutCorrections_LeavesTheSpaceExactlyAsItStands()
    {
        var stall = Stall.Create(Guid.NewGuid(), "5", 900m, ApplicableFees.BaseRental);
        stall.UpdateRates(900m, dailyRate: 30m);
        stall.UpdateAreaInfo(4.0, "Extension", remarks: "By ordinance");
        stall.Contracts.Add(Contract.Create(stall.Id, "Maria Santos", null, new DateOnly(2024, 1, 1), 1, 900m));

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdWithContractsAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        stallRepo.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var payorRepo = new Mock<IPayorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("tester");
        var uow = new Mock<IUnitOfWork>();

        var handler = Build(stallRepo, payorRepo, currentUser, uow);

        var result = await handler.Handle(
            new RenewStallContractCommand(stall.Id, new DateOnly(2026, 6, 28), 3, "Maria Santos", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(900m, stall.MonthlyRate);
        Assert.Equal(30m, stall.DailyRate);          // the ordinance daily fee survives the renewal
        Assert.Equal(4.0, stall.AreaSqm);
        Assert.Equal("Extension", stall.AreaNote);
        Assert.Equal("By ordinance", stall.Remarks);
    }

    /// <summary>A corrected area must not silently drop the daily fee or the office's remarks either.</summary>
    [Fact]
    public async Task Renew_WithCorrectedArea_KeepsTheDailyFeeAndRemarks()
    {
        var stall = Stall.Create(Guid.NewGuid(), "5", 900m, ApplicableFees.BaseRental);
        stall.UpdateRates(900m, dailyRate: 30m);
        stall.UpdateAreaInfo(4.0, "Extension", remarks: "By ordinance");
        stall.Contracts.Add(Contract.Create(stall.Id, "Maria Santos", null, new DateOnly(2024, 1, 1), 1, 900m));

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdWithContractsAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        stallRepo.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var payorRepo = new Mock<IPayorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("tester");
        var uow = new Mock<IUnitOfWork>();

        var handler = Build(stallRepo, payorRepo, currentUser, uow);

        var result = await handler.Handle(
            new RenewStallContractCommand(stall.Id, new DateOnly(2026, 6, 28), 3, "Maria Santos", null,
                AreaSqm: 5.25),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5.25, stall.AreaSqm);
        Assert.Equal("Extension", stall.AreaNote);   // untouched note kept
        Assert.Equal("By ordinance", stall.Remarks);
        Assert.Equal(30m, stall.DailyRate);
    }
}
