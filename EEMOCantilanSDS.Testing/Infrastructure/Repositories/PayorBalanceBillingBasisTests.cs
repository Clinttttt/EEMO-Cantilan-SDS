using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Moq;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// What the payor's own portal says a space costs.
///
/// <para>
/// A market stall was shown its monthly rate, ₱900, beside a balance built from days, ₱210. The payor is never billed ₱900:
/// they are billed a day's fee for each day they traded, and no amount of arithmetic on the screen could reconcile the two.
/// The balance was right all along; the rate beside it was the wrong rule.
/// </para>
/// </summary>
public class PayorBalanceBillingBasisTests : RepositoryTestBase
{
    [Fact]
    public async Task AMarketStallStatesADaysFeeAndTheDaysItOwes()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Kim Chui", "Kim Chui", PhilippineTime.Today.AddMonths(-1), 3, 900m);

        ctx.AddRange(npm, stall, contract, PayorStallLink.Create(payor, stall.Id));
        await ctx.SaveChangesAsync();

        // Seven days owed, as the settlement service works them out; the repository must not re-derive them.
        var settlement = new Mock<INpmMonthSettlementService>();
        settlement.Setup(s => s.ComputePayableAsync(It.IsAny<Stall>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new NpmMonthPayable(7, 210m));

        var repo = new PayorRepository(ctx, settlement.Object);
        var balance = Assert.Single(await repo.GetBalancesAsync(payor));

        Assert.True(balance.IsDailyBilled);
        Assert.Equal(30m, balance.DailyRate);          // the office's own daily fee, not the monthly rate
        Assert.Equal(7, balance.DaysOwed);
        Assert.Equal(900m, balance.MonthlyRate);       // still carried, for the facilities that are billed by it
    }

    [Fact]
    public async Task AMonthlySpaceIsUnchanged()
    {
        // The other facilities ARE billed by the month, and their card must keep saying so.
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();

        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(tcc.Id, "B-1", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Juan Cruz", "Juan Cruz", PhilippineTime.Today.AddMonths(-1), 3, 2400m);

        ctx.AddRange(tcc, stall, contract, PayorStallLink.Create(payor, stall.Id));
        await ctx.SaveChangesAsync();

        var repo = new PayorRepository(ctx, Mock.Of<INpmMonthSettlementService>());
        var balance = Assert.Single(await repo.GetBalancesAsync(payor));

        Assert.False(balance.IsDailyBilled);
        Assert.Equal(0m, balance.DailyRate);
        Assert.Equal(0, balance.DaysOwed);
        Assert.Equal(2400m, balance.MonthlyRate);
    }
}
