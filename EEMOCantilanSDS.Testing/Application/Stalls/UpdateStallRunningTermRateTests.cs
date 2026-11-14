using EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>While a term runs, every month is charged at its agreed rate (IA-077), so the space's own rate cannot be moved away from it to leave two figures for one space.</summary>
public class UpdateStallRunningTermRateTests
{
    private static Stall Space(int yearsAgo, int years)
    {
        var stall = Stall.Create(Guid.NewGuid(), "3", 900m, ApplicableFees.BaseRental);
        stall.Contracts.Add(Contract.Create(stall.Id, "Occupant", null, PhilippineTime.Today.AddYears(-yearsAgo), years, 900m));
        return stall;
    }

    private static Task<Result<StallDto>> Edit(Stall stall, decimal rate)
    {
        var stalls = new Mock<IStallRepository>();
        stalls.Setup(r => r.GetByIdWithContractsAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        stalls.Setup(r => r.UpdateAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new UpdateStallCommandHandler(stalls.Object, new Mock<IUnitOfWork>().Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant)
            .Handle(new(stall.Id, rate, null, AreaSqm: 4, AreaNote: null, DailyRate: null, ActualOccupant: "Occupant", NameOnContract: null, Remarks: null), CancellationToken.None);
    }

    [Fact]
    public async Task ChangingTheRate_IsRefused_WhileATermRuns()
    {
        var stall = Space(yearsAgo: 1, years: 3);

        var result = await Edit(stall, 1000m);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(900m, stall.MonthlyRate);
    }

    [Fact]
    public async Task AnEditThatLeavesTheRateAlone_StillSaves()
    {
        Assert.True((await Edit(Space(yearsAgo: 1, years: 3), 900m)).IsSuccess);
    }

    [Fact]
    public async Task ASpaceWhoseTermHasRunOut_CanHaveItsRateSet_ForTheNextTerm()
    {
        var stall = Space(yearsAgo: 2, years: 1);

        Assert.True((await Edit(stall, 1000m)).IsSuccess);
        Assert.Equal(1000m, stall.MonthlyRate);
    }
}
