using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
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
    /// <summary>A contract from here has no elapsed month behind it, so a test about the month in progress sees only that month.</summary>
    private static DateOnly ThisMonth => new(PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1);

    private static BillingPolicyVersion CountingFrom(DateOnly month) => BillingPolicyVersion.Create(Guid.NewGuid(), PhilippineTime.Today, PhilippineTime.Today,
        BillingPolicy.StallTrackDefaults with { CountsMonthsFrom = month }, null, Guid.NewGuid(), DateTime.UtcNow);

    [Fact]
    public async Task AMonthWithNoRecord_FromBeforeTheRulesBegin_IsNotAskedOfThePayor()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var tcc = Facility.Create(FacilityCode.TCC, "Town Center", "TCC");
        var stall = Stall.Create(tcc.Id, "7", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Occupant", "Occupant", ThisMonth.AddMonths(-6), 3, 2400m);
        ctx.AddRange(tcc, stall, contract, PayorStallLink.Create(payor, stall.Id), CountingFrom(ThisMonth));
        await ctx.SaveChangesAsync();

        var item = Assert.Single(await new PayorRepository(ctx, new Mock<INpmMonthSettlementService>().Object).GetPayableItemsAsync(payor));

        Assert.Equal((ThisMonth.Year, ThisMonth.Month, true), (item.Year, item.Month, item.PayableOnline));
        // The office still sees all of it, and can still take it.
        var office = await new FacilityReportsRepository(ctx).GetStallStatementAsync(stall.Id, CancellationToken.None);
        Assert.Equal(6, office!.UnpaidMonths.Count);
        Assert.Equal(6 * 2400m, office.BalanceBeforeRules);
    }

    [Fact]
    public async Task ARecordedMonth_FromBeforeTheRulesBegin_IsStillPayable_AndHoldsNothingUp()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var tcc = Facility.Create(FacilityCode.TCC, "Town Center", "TCC");
        var stall = Stall.Create(tcc.Id, "7", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Occupant", "Occupant", ThisMonth.AddMonths(-6), 3, 2400m);
        var old = ThisMonth.AddMonths(-4);
        var recorded = PaymentRecord.Create(stall.Id, old.Year, old.Month, 2400m, "test");
        recorded.MarkPartiallyPaidOnline(400m, "test");
        ctx.AddRange(tcc, stall, contract, recorded, PayorStallLink.Create(payor, stall.Id), CountingFrom(ThisMonth));
        await ctx.SaveChangesAsync();

        var items = await new PayorRepository(ctx, new Mock<INpmMonthSettlementService>().Object).GetPayableItemsAsync(payor);

        var listedOld = items.Single(i => i.Period == recorded.PeriodKey);
        Assert.Equal((2000m, true, null), (listedOld.BalanceDue, listedOld.PayableOnline, listedOld.PayFirst));
        var current = items.Single(i => i.Year == ThisMonth.Year && i.Month == ThisMonth.Month);
        Assert.Equal((true, null), (current.PayableOnline, current.PayFirst));
    }

    [Fact]
    public async Task WhileTheStartMonthIsStillAhead_NextMonthIsNotOffered_OverTheMonthInProgress()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var tcc = Facility.Create(FacilityCode.TCC, "Town Center", "TCC");
        var stall = Stall.Create(tcc.Id, "7", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Occupant", "Occupant", ThisMonth, 3, 2400m);
        ctx.AddRange(tcc, stall, contract, PayorStallLink.Create(payor, stall.Id), CountingFrom(ThisMonth.AddMonths(2)));
        await ctx.SaveChangesAsync();

        var item = Assert.Single(await new PayorRepository(ctx, new Mock<INpmMonthSettlementService>().Object).GetPayableItemsAsync(payor));

        Assert.Equal((ThisMonth.Year, ThisMonth.Month), (item.Year, item.Month));
    }

    [Fact]
    public async Task AMonthNobodyRecorded_IsOwedOnThePortal_AsThePreviousBalance()
    {
        // Nothing writes a record until money moves, so a month nobody paid had no row here while the office counted it: two totals for one account.
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var tcc = Facility.Create(FacilityCode.TCC, "Town Center", "TCC");
        var stall = Stall.Create(tcc.Id, "7", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Occupant", "Occupant", ThisMonth.AddMonths(-3), 3, 2400m);
        ctx.AddRange(tcc, stall, contract, PayorStallLink.Create(payor, stall.Id), CountingFrom(ThisMonth.AddMonths(-3)));
        await ctx.SaveChangesAsync();

        var repo = new PayorRepository(ctx, new Mock<INpmMonthSettlementService>().Object);
        var items = await repo.GetPayableItemsAsync(payor);

        Assert.Equal(4, items.Count);
        Assert.Equal(3, items.Count(i => i.PreviousBalance));
        Assert.False(items.Single(i => i.Period == $"{ThisMonth.Year:0000}-{ThisMonth.Month:00}").PreviousBalance);
        Assert.All(items, i => Assert.Equal((PayorPayableKind.Monthly, 2400m), (i.Kind, i.BalanceDue)));
        var oldest = ThisMonth.AddMonths(-3);
        Assert.Equal($"{oldest.Year:0000}-{oldest.Month:00}", Assert.Single(items, i => i.PayableOnline).Period);
        Assert.Equal(9600m, Assert.Single(await repo.GetBalancesAsync(payor)).OutstandingBalance);
    }

    [Fact]
    public async Task AMonthWithAnUnpaidRecord_IsListedOnce_AtTheRecordsBalance()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var tcc = Facility.Create(FacilityCode.TCC, "Town Center", "TCC");
        var stall = Stall.Create(tcc.Id, "7", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Occupant", "Occupant", ThisMonth.AddMonths(-2), 3, 2400m);
        var lastMonth = ThisMonth.AddMonths(-1);
        var partial = PaymentRecord.Create(stall.Id, lastMonth.Year, lastMonth.Month, 2400m, "test");
        partial.MarkPartiallyPaidOnline(400m, "test");
        ctx.AddRange(tcc, stall, contract, partial, PayorStallLink.Create(payor, stall.Id), CountingFrom(ThisMonth.AddMonths(-2)));
        await ctx.SaveChangesAsync();

        var items = await new PayorRepository(ctx, new Mock<INpmMonthSettlementService>().Object).GetPayableItemsAsync(payor);

        var listed = Assert.Single(items, i => i.Period == partial.PeriodKey);
        Assert.Equal(2000m, listed.BalanceDue);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task AMarketStallsElapsedMonth_IsListedReadOnly_AtWhatTheOfficeSaysIsUnpaid()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "4", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Kim Chui", "Kim Chui", ThisMonth.AddMonths(-1), 3, 900m);
        ctx.AddRange(npm, stall, contract, PayorStallLink.Create(payor, stall.Id), CountingFrom(ThisMonth.AddMonths(-1)));
        await ctx.SaveChangesAsync();

        var settlement = new Mock<INpmMonthSettlementService>();
        settlement.Setup(s => s.ComputeWholeMonthPayableAsync(It.IsAny<Stall>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new NpmMonthPayable(0, 0m));

        var item = Assert.Single(await new PayorRepository(ctx, settlement.Object).GetPayableItemsAsync(payor));

        var office = await new FacilityReportsRepository(ctx).GetStallStatementAsync(stall.Id, CancellationToken.None);
        Assert.Equal(Assert.Single(office!.UnpaidMonths).Unpaid, item.BalanceDue);
        Assert.True(item.BalanceDue > 0m);
        Assert.Equal((PayorPayableKind.NpmDaily, false, true), (item.Kind, item.PayableOnline, item.PreviousBalance));
    }

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
    public async Task AFishStallOwesTheBaseFeeForTheDaysItOwes()
    {
        // The fish section's own screen read ₱0.00 beside "2 days owed this month", while the office's stall profile read
        // ₱60 for the same space on the same day. The days are owed; only the weighing fee waits on a declaration.
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var contract = Contract.Create(stall.Id, "Godon Lar", "Godon Lar", ThisMonth, 3, 900m);

        ctx.AddRange(npm, stall, contract, PayorStallLink.Create(payor, stall.Id));
        await ctx.SaveChangesAsync();

        var settlement = new Mock<INpmMonthSettlementService>();
        settlement.Setup(s => s.ComputePayableAsync(It.IsAny<Stall>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new NpmMonthPayable(2, 60m));
        settlement.Setup(s => s.GetPayableDaysAsync(It.IsAny<Stall>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new[] { PhilippineTime.Today.AddDays(-1), PhilippineTime.Today });
        settlement.Setup(s => s.QuoteFishDayAsync(It.IsAny<Stall>(), It.IsAny<DateOnly>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(NpmFishDayQuote.Payable(30m, 30m, 0m));

        var repo = new PayorRepository(ctx, settlement.Object);
        var balance = Assert.Single(await repo.GetBalancesAsync(payor));

        Assert.True(balance.IsDailyBilled);
        Assert.Equal(2, balance.DaysOwed);
        Assert.Equal(30m, balance.DailyRate);
        Assert.Equal(60m, balance.OutstandingBalance);   // the office's own figure, not zero
    }

    [Fact]
    public async Task ANonFishMarketStallIsNotCountedTwice()
    {
        // Every other section IS billed as one figure for the month, and that figure is already in its payable item.
        // Adding the settlement amount again here would have doubled what the payor is asked for.
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "4", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Kim Chui", "Kim Chui", ThisMonth, 3, 900m);

        ctx.AddRange(npm, stall, contract, PayorStallLink.Create(payor, stall.Id));
        await ctx.SaveChangesAsync();

        var settlement = new Mock<INpmMonthSettlementService>();
        settlement.Setup(s => s.ComputePayableAsync(It.IsAny<Stall>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new NpmMonthPayable(2, 60m));
        // The stall's payable is the rest of the month's rent (₱900 less ₱60 collected), not the elapsed days.
        settlement.Setup(s => s.ComputeWholeMonthPayableAsync(It.IsAny<Stall>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new NpmMonthPayable(28, 840m));

        var repo = new PayorRepository(ctx, settlement.Object);
        var balance = Assert.Single(await repo.GetBalancesAsync(payor));
        var item = Assert.Single(await repo.GetPayableItemsAsync(payor));

        Assert.Equal(840m, balance.OutstandingBalance);
        Assert.Equal(840m, item.BalanceDue);
        Assert.Equal(PayorPayableKind.NpmDaily, item.Kind);
        Assert.Null(item.Days);       // a month's remainder, not days counted out
    }

    // Only monthly Stall Rent is payable online. A daily-billed market's balance (whole-month aggregate included) stays listed but read-only.
    [Fact]
    public async Task DailyBilledMarketBalance_IsListedButNotPayableOnline()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "4", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Kim Chui", "Kim Chui", ThisMonth, 3, 900m);
        ctx.AddRange(npm, stall, contract, PayorStallLink.Create(payor, stall.Id));
        await ctx.SaveChangesAsync();

        var settlement = new Mock<INpmMonthSettlementService>();
        settlement.Setup(s => s.ComputeWholeMonthPayableAsync(It.IsAny<Stall>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new NpmMonthPayable(28, 840m));

        var item = Assert.Single(await new PayorRepository(ctx, settlement.Object).GetPayableItemsAsync(payor));

        Assert.Equal(840m, item.BalanceDue);   // still visible
        Assert.False(item.PayableOnline);
    }

    [Fact]
    public async Task AnOfficeThatTakesAnyMonth_ListsEveryEarlierPeriod_EachPayableByItself()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var tcc = Facility.Create(FacilityCode.TCC, "Town Center", "TCC");
        var stall = Stall.Create(tcc.Id, "7", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Occupant", "Occupant", PhilippineTime.Today.AddMonths(-6), 3, 2400m);
        var earlier = PaymentRecord.Create(stall.Id, PhilippineTime.Today.AddMonths(-2).Year, PhilippineTime.Today.AddMonths(-2).Month, 2400m, "test");
        var previous = PaymentRecord.Create(stall.Id, PhilippineTime.Today.AddMonths(-1).Year, PhilippineTime.Today.AddMonths(-1).Month, 2400m, "test");
        ctx.AddRange(tcc, stall, contract, earlier, previous, PayorStallLink.Create(payor, stall.Id), AnyMonthPolicy());
        await ctx.SaveChangesAsync();

        var items = await new PayorRepository(ctx, new Mock<INpmMonthSettlementService>().Object).GetPayableItemsAsync(payor);

        Assert.True(items.Count >= 3);   // two earlier periods + the current month: nothing is auto-spread or hidden
        Assert.All(items, i => Assert.True(i.PayableOnline));
        Assert.Equal(items.Count, items.Select(i => i.Period).Distinct().Count());
    }

    private static BillingPolicyVersion AnyMonthPolicy() => BillingPolicyVersion.Create(Guid.NewGuid(), PhilippineTime.Today, PhilippineTime.Today,
        BillingPolicy.StallTrackDefaults with { OldestMonthFirst = false }, null, Guid.NewGuid(), DateTime.UtcNow);

    [Fact]
    public async Task MonthlyRecordNotOnLegacyAuthority_IsNotOfferedOnline()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var tcc = Facility.Create(FacilityCode.TCC, "Town Center", "TCC");
        var stall = Stall.Create(tcc.Id, "7", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Occupant", "Occupant", PhilippineTime.Today.AddMonths(-6), 3, 2400m);
        var pending = PaymentRecord.Create(stall.Id, PhilippineTime.Today.AddMonths(-1).Year, PhilippineTime.Today.AddMonths(-1).Month, 2400m, "test");
        pending.MarkSettlementPendingCutover();
        ctx.AddRange(tcc, stall, contract, pending, PayorStallLink.Create(payor, stall.Id));
        await ctx.SaveChangesAsync();

        var items = await new PayorRepository(ctx, new Mock<INpmMonthSettlementService>().Object).GetPayableItemsAsync(payor);

        Assert.False(items.Single(i => i.Period == pending.PeriodKey).PayableOnline);
    }

    [Fact]
    public async Task APeriodWithACaptureUnderReview_IsNotOfferedOnline_AndSaysSo()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var tcc = Facility.Create(FacilityCode.TCC, "Town Center", "TCC");
        var stall = Stall.Create(tcc.Id, "7", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Occupant", "Occupant", PhilippineTime.Today.AddMonths(-6), 3, 2400m);
        var month = PhilippineTime.Today.AddMonths(-1);
        var earlier = PhilippineTime.Today.AddMonths(-2);
        var underReview = PaymentRecord.Create(stall.Id, month.Year, month.Month, 2400m, "test");
        var ordinary = PaymentRecord.Create(stall.Id, earlier.Year, earlier.Month, 2400m, "test");
        var capture = OnlinePaymentTransaction.Create("EEMO-OP-REVIEW", payor, underReview.Id, 2400m, "PayMongo");
        capture.SetPending("cs_review", "https://checkout");
        capture.MarkPaid("pay_review", "gcash", DateTime.UtcNow, "{}");
        capture.MarkSettlementReconciliationRequired("needs treasury review");
        ctx.AddRange(tcc, stall, contract, underReview, ordinary, capture, PayorStallLink.Create(payor, stall.Id), AnyMonthPolicy());
        await ctx.SaveChangesAsync();

        var items = await new PayorRepository(ctx, new Mock<INpmMonthSettlementService>().Object).GetPayableItemsAsync(payor);

        var held = items.Single(i => i.Period == underReview.PeriodKey);
        Assert.False(held.PayableOnline);
        Assert.True(held.PaymentUnderReview);
        var open = items.Single(i => i.Period == ordinary.PeriodKey);
        Assert.True(open.PayableOnline);
        Assert.False(open.PaymentUnderReview);

        // Once the Head has decided, the period is an ordinary balance again and can be paid.
        capture.RecordOfficeDecision(OnlinePaymentDisposition.Refunded, "ref_1", null, "head", DateTime.UtcNow);
        await ctx.SaveChangesAsync();
        var after = await new PayorRepository(ctx, new Mock<INpmMonthSettlementService>().Object).GetPayableItemsAsync(payor);
        var released = after.Single(i => i.Period == underReview.PeriodKey);
        Assert.True(released.PayableOnline);
        Assert.False(released.PaymentUnderReview);
    }

    [Fact]
    public async Task AMonthlySpaceIsOfferedAtItsContractRate_NotTheStallsCurrentRate()
    {
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();

        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(tcc.Id, "B-2", 3000m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Juan Cruz", "Juan Cruz", ThisMonth, 3, 2400m);

        ctx.AddRange(tcc, stall, contract, PayorStallLink.Create(payor, stall.Id));
        await ctx.SaveChangesAsync();

        var repo = new PayorRepository(ctx, Mock.Of<INpmMonthSettlementService>());
        var item = Assert.Single(await repo.GetPayableItemsAsync(payor));

        Assert.Equal(2400m, item.BalanceDue);
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

    [Fact]
    public async Task ACustomFacilityIsNamedByTheOfficesOwnNameAndAcronym_NotItsInternalSlotCode()
    {
        // The portal printed "Custom1": the DTO exposed only the enum slot. The wording comes from the facility record of the stall's own tenant;
        // the code stays on the DTO as identity.
        await using var ctx = NewContext();
        var payor = Guid.NewGuid();
        var restRooms = Facility.Create(FacilityCode.Custom1, "Rest Rooms", "RR");
        var coldStorage = Facility.Create(FacilityCode.Custom2, "Cold Storage", string.Empty);          // no acronym saved
        var first = Stall.Create(restRooms.Id, "2", 900m, ApplicableFees.BaseRental);
        var second = Stall.Create(coldStorage.Id, "7", 1_500m, ApplicableFees.BaseRental);
        ctx.AddRange(restRooms, coldStorage, first, second,
            Contract.Create(first.Id, "Robin Padilla", "Robin Padilla", ThisMonth, 3, 900m),
            Contract.Create(second.Id, "Robin Padilla", "Robin Padilla", ThisMonth, 3, 1_500m),
            PayorStallLink.Create(payor, first.Id), PayorStallLink.Create(payor, second.Id));
        await ctx.SaveChangesAsync();

        var repo = new PayorRepository(ctx, new Mock<INpmMonthSettlementService>().Object);
        var balances = (await repo.GetBalancesAsync(payor)).ToDictionary(b => b.Facility);

        Assert.Equal(("Rest Rooms", "RR"), (balances[FacilityCode.Custom1].FacilityName, balances[FacilityCode.Custom1].FacilityShortName));
        Assert.Equal(("Cold Storage", (string?)null), (balances[FacilityCode.Custom2].FacilityName, balances[FacilityCode.Custom2].FacilityShortName));
        Assert.Equal(FacilityCode.Custom1, balances[FacilityCode.Custom1].Facility);                     // identity unchanged

        // The payable items (the Balances page) carry the same wording, per stall.
        var items = (await repo.GetPayableItemsAsync(payor)).ToDictionary(i => i.Facility);
        Assert.Equal(("Rest Rooms", "RR"), (items[FacilityCode.Custom1].FacilityName, items[FacilityCode.Custom1].FacilityShortName));
        Assert.Equal(("Cold Storage", (string?)null), (items[FacilityCode.Custom2].FacilityName, items[FacilityCode.Custom2].FacilityShortName));
    }
}
