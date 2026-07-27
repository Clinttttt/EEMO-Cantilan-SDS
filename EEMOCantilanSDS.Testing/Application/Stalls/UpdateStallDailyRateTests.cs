using EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Updating a stall must never move the daily rate unless a rate was actually supplied.
///
/// Regression: several screens edit a stall without showing its daily rate — the stall profile sent null
/// (wiping it) while the vendor registry and the NPM page sent a hardcoded ₱30. For a per-LGU CUSTOM
/// section, whose own <c>DailyRate</c> is what billing charges via <see cref="Stall.ResolveDailyFee"/>,
/// renaming an occupant could therefore silently change the money — a ₱25 section became ₱30, and a ₱40
/// municipality's stall was stamped with Cantilan's ₱30.
/// </summary>
public class UpdateStallDailyRateTests
{
    private static UpdateStallCommandHandler Build(Mock<IStallRepository> stallRepo, Mock<IUnitOfWork> uow)
        => new(stallRepo.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

    private static (Stall Stall, Mock<IStallRepository> Repo, Mock<IUnitOfWork> Uow) Fixture(decimal? dailyRate)
    {
        var stall = Stall.Create(
            Guid.NewGuid(), "1", 900m, ApplicableFees.DailyRental,
            dailyRate: dailyRate, customSectionName: "Sari-sari Area");
        stall.Contracts.Add(Contract.Create(stall.Id, "Diego Brando", "Diego Brando", new DateOnly(2026, 1, 1), 3, 900m));

        var repo = new Mock<IStallRepository>();
        repo.Setup(r => r.GetByIdWithContractsAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        return (stall, repo, new Mock<IUnitOfWork>());
    }

    private static UpdateStallCommand Command(Guid stallId, decimal? dailyRate) => new(
        StallId: stallId,
        MonthlyRate: 900m,
        Fees: ApplicableFees.DailyRental,
        AreaSqm: null,
        AreaNote: null,
        DailyRate: dailyRate,
        ActualOccupant: "Diego Brando",
        NameOnContract: "Diego Brando",
        Remarks: null);

    [Fact]
    public async Task NullDailyRate_PreservesTheStoredRate()
    {
        var (stall, repo, uow) = Fixture(dailyRate: 25m);

        var result = await Build(repo, uow).Handle(Command(stall.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(25m, stall.DailyRate);              // not wiped, not overwritten
        Assert.Equal(25m, result.Value!.DailyRate);      // and the response reports the effective rate
        Assert.Equal(25m, stall.ResolveDailyFee(40m));   // the custom section still bills its own rate
    }

    [Fact]
    public async Task SuppliedDailyRate_StillUpdatesIt()
    {
        // The rate remains editable where the screen genuinely offers it.
        var (stall, repo, uow) = Fixture(dailyRate: 25m);

        var result = await Build(repo, uow).Handle(Command(stall.Id, 28m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(28m, stall.DailyRate);
        Assert.Equal(28m, result.Value!.DailyRate);
    }

    [Fact]
    public async Task NullDailyRate_OnAStallThatNeverHadOne_StaysNull()
    {
        var (stall, repo, uow) = Fixture(dailyRate: null);

        var result = await Build(repo, uow).Handle(Command(stall.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(stall.DailyRate);
        Assert.Equal(40m, stall.ResolveDailyFee(40m));   // falls back to the tenant's ordinance rate
    }
}
