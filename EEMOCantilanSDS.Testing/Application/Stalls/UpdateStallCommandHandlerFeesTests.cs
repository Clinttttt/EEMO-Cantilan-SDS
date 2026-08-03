using EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Which charges apply to a space is part of its record, and the office edits it on the vendor form: a meter
/// installed after the space was let has to start being billed, and one removed has to stop.
///
/// The command carried the charges from the start but the handler never wrote them, so "Add a utility charge"
/// saved and changed nothing — the meter-reading dialog went on saying the stall was not billed for electricity
/// or water. These tests pin both halves: a stated set is applied, and an unstated one leaves the record alone.
/// </summary>
public class UpdateStallCommandHandlerFeesTests
{
    private static Stall LetSpace(ApplicableFees fees)
    {
        var stall = Stall.Create(Guid.NewGuid(), "3", 900m, fees, section: MarketSection.VegetableArea);
        stall.Contracts.Add(Contract.Create(stall.Id, "Maria Santos", null, new DateOnly(2026, 1, 1), 3, 900m));
        return stall;
    }

    private static (UpdateStallCommandHandler Handler, Mock<IUnitOfWork> Uow) Build(Stall stall)
    {
        var stalls = new Mock<IStallRepository>();
        stalls.Setup(r => r.GetByIdWithContractsAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        stalls.Setup(r => r.UpdateAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var uow = new Mock<IUnitOfWork>();
        return (new UpdateStallCommandHandler(stalls.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant), uow);
    }

    private static UpdateStallCommand Command(Guid stallId, ApplicableFees? fees) =>
        new(stallId, 900m, fees, AreaSqm: 4, AreaNote: null, DailyRate: null,
            ActualOccupant: "Maria Santos", NameOnContract: null, Remarks: null);

    [Fact]
    public async Task AddingAUtilityCharge_PutsItOnTheSpace()
    {
        var stall = LetSpace(ApplicableFees.BaseRental);
        var (handler, uow) = Build(stall);

        var result = await handler.Handle(
            Command(stall.Id, ApplicableFees.BaseRental | ApplicableFees.Water), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(stall.Fees.HasFlag(ApplicableFees.Water));
        Assert.False(stall.Fees.HasFlag(ApplicableFees.Electricity));
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemovingAUtilityCharge_StopsItBeingBilled()
    {
        var stall = LetSpace(ApplicableFees.BaseRental | ApplicableFees.Electricity | ApplicableFees.Water);
        var (handler, _) = Build(stall);

        await handler.Handle(Command(stall.Id, ApplicableFees.BaseRental | ApplicableFees.Water), CancellationToken.None);

        Assert.False(stall.Fees.HasFlag(ApplicableFees.Electricity));
        Assert.True(stall.Fees.HasFlag(ApplicableFees.Water));
    }

    [Fact]
    public async Task TheBaseRental_IsNeverDroppedByAnEdit()
    {
        // A let space always owes rent, whatever a caller sends.
        var stall = LetSpace(ApplicableFees.BaseRental | ApplicableFees.Water);
        var (handler, _) = Build(stall);

        await handler.Handle(Command(stall.Id, ApplicableFees.Water), CancellationToken.None);

        Assert.True(stall.Fees.HasFlag(ApplicableFees.BaseRental));
    }

    [Fact]
    public async Task AScreenThatDoesNotEditTheCharges_LeavesThemAlone()
    {
        // Null means "not supplied" — the same rule the daily rate already follows, so an occupant-name edit
        // from a screen with no utility controls cannot strip a meter off the record.
        var stall = LetSpace(ApplicableFees.BaseRental | ApplicableFees.Electricity | ApplicableFees.FishFee);
        var (handler, _) = Build(stall);

        await handler.Handle(Command(stall.Id, fees: null), CancellationToken.None);

        Assert.True(stall.Fees.HasFlag(ApplicableFees.Electricity));
        Assert.True(stall.Fees.HasFlag(ApplicableFees.FishFee));
    }
}
