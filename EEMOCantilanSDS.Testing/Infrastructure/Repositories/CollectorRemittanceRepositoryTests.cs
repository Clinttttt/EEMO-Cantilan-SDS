using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// The figure a remittance is checked against, and the rules that keep "not yet remitted" exact.
///
/// <para>
/// The office was explicit that a remittance may never exceed what was collected, which makes this total the ceiling on a
/// money record. Three things about it are easy to get wrong and are pinned here: it counts the money on the day it was
/// TAKEN rather than the day a fee was for, it leaves out the electricity and water the office banks separately, and a
/// voided remittance counts for nothing.
/// </para>
/// </summary>
public class CollectorRemittanceRepositoryTests : RepositoryTestBase
{
    private static readonly DateOnly Aug20 = new(2026, 8, 20);
    private static readonly DateOnly Aug24 = new(2026, 8, 24);
    private static readonly DateOnly Aug25 = new(2026, 8, 25);

    [Fact]
    public async Task CountsADaySettledLateOnTheDayTheMoneyWasTaken()
    {
        // A payor clearing days they owed hands the cash over now. Matching on the fee's own day would leave that money
        // permanently unremittable, because its day is already covered by an earlier remittance.
        await using var ctx = NewContext();
        var me = Guid.NewGuid();

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        ctx.AddRange(npm, stall);

        // Three owed days, all settled on Aug 24.
        foreach (var feeDay in new[] { new DateOnly(2026, 8, 22), new DateOnly(2026, 8, 23), Aug24 })
        {
            var d = DailyCollection.Create(stall.Id, feeDay, dailyFee: 30m);
            d.MarkPaid("OR-90", me);
            SetTakenAt(d, Aug24);
            ctx.Add(d);
        }
        await ctx.SaveChangesAsync();

        var repo = new CollectorRemittanceRepository(ctx);

        // The whole ₱90 answers for Aug 24, the day it was taken.
        Assert.Equal(90m, await repo.GetFeeCollectionsTotalAsync(me, Aug24, Aug24));

        // And nothing is attributed to the days the fees were for.
        Assert.Equal(0m, await repo.GetFeeCollectionsTotalAsync(me, new DateOnly(2026, 8, 22), new DateOnly(2026, 8, 23)));
    }

    [Fact]
    public async Task LeavesOutTheElectricityAndWaterTheOfficeBanksSeparately()
    {
        // A monthly bill carries the rent and the meters together under one receipt. Only the rent is this collector's
        // fee accountability; utilities are additional income, banked apart.
        await using var ctx = NewContext();
        var me = Guid.NewGuid();

        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(tcc.Id, "B-1", 2400m, ApplicableFees.BaseRental);
        var payment = PaymentRecord.Create(stall.Id, 2026, 8, 2400m);
        payment.RecordPayment("OR-1", me, PaymentStatus.Paid, elecAmount: 620m, waterAmount: 180m);

        ctx.AddRange(tcc, stall, payment);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRemittanceRepository(ctx);

        Assert.Equal(2400m, await repo.GetFeeCollectionsTotalAsync(me, Aug20, Aug25));
    }

    [Fact]
    public async Task APartPaymentIsAppliedToTheFeeChargeAndCappedThere()
    {
        // With no split recorded, money received is applied to the fee charge first. The figure can then never claim more
        // fee money than the fees themselves came to, and the excess belongs to the utilities.
        await using var ctx = NewContext();
        var me = Guid.NewGuid();

        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var underFees = Stall.Create(tcc.Id, "B-1", 2400m, ApplicableFees.BaseRental);
        var overFees = Stall.Create(tcc.Id, "B-2", 2400m, ApplicableFees.BaseRental);

        // ₱1,000 of a ₱2,400 rent: all of it is fee money.
        var partial = PaymentRecord.Create(underFees.Id, 2026, 8, 2400m);
        partial.RecordPayment("OR-2", me, PaymentStatus.Partial, partialAmount: 1000m, elecAmount: 600m);

        // ₱2,700 against a ₱2,400 rent plus ₱600 of meters: the rent is covered and ₱300 is utilities.
        var beyond = PaymentRecord.Create(overFees.Id, 2026, 8, 2400m);
        beyond.RecordPayment("OR-3", me, PaymentStatus.Partial, partialAmount: 2700m, elecAmount: 600m);

        ctx.AddRange(tcc, underFees, overFees, partial, beyond);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRemittanceRepository(ctx);

        Assert.Equal(1000m + 2400m, await repo.GetFeeCollectionsTotalAsync(me, Aug20, Aug25));
    }

    [Fact]
    public async Task AVoidedRemittanceCountsForNothingAndFreesItsDays()
    {
        // A remittance recorded in error is withdrawn with a reason rather than deleted. The money trail stays readable
        // and the correct remittance can be filed over the same days.
        await using var ctx = NewContext();
        var me = Guid.NewGuid();
        var officer = Guid.NewGuid();

        var wrong = CollectorRemittance.Create(me, 500m, DateTime.UtcNow, Aug20, Aug24, officer, "head", "RCD-1", null, "head");
        ctx.Add(wrong);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRemittanceRepository(ctx);
        Assert.Equal(500m, await repo.GetRemittedTotalAsync(me, Aug20, Aug24));
        Assert.NotNull(await repo.FindOverlappingAsync(me, Aug24, Aug25));

        wrong.Void("Wrong collector.", "head");
        await ctx.SaveChangesAsync();

        Assert.Equal(0m, await repo.GetRemittedTotalAsync(me, Aug20, Aug24));
        Assert.Null(await repo.FindOverlappingAsync(me, Aug24, Aug25));
        Assert.Equal("Wrong collector.", wrong.VoidReason);
        Assert.True(wrong.IsDeleted);
    }

    [Fact]
    public async Task OverlapIsFoundWhereverTheDaysTouch()
    {
        // Overlap is the only thing standing between an exact "not yet remitted" and a guess, so any touching of the days
        // counts: the same day, a range inside another, and a range that straddles it.
        await using var ctx = NewContext();
        var me = Guid.NewGuid();
        var officer = Guid.NewGuid();

        ctx.Add(CollectorRemittance.Create(me, 300m, DateTime.UtcNow, Aug20, Aug24, officer, "head", null, null, "head"));
        await ctx.SaveChangesAsync();

        var repo = new CollectorRemittanceRepository(ctx);

        Assert.NotNull(await repo.FindOverlappingAsync(me, Aug24, Aug25));                               // last day shared
        Assert.NotNull(await repo.FindOverlappingAsync(me, new DateOnly(2026, 8, 21), new DateOnly(2026, 8, 22))); // inside
        Assert.NotNull(await repo.FindOverlappingAsync(me, new DateOnly(2026, 8, 1), Aug25));            // straddles
        Assert.Null(await repo.FindOverlappingAsync(me, Aug25, Aug25));                                  // clear of it

        // Another collector's remittance is not this collector's business.
        Assert.Null(await repo.FindOverlappingAsync(Guid.NewGuid(), Aug20, Aug24));
    }

    /// <summary>
    /// Puts the collection's timestamp on a chosen Philippine day. The repository selects on when the money was taken, so
    /// a test about that has to be able to say when it was.
    /// </summary>
    private static void SetTakenAt(DailyCollection collection, DateOnly philippineDay)
    {
        var (startUtc, _) = PhilippineTime.DayUtcRange(philippineDay);
        typeof(DailyCollection).GetProperty(nameof(DailyCollection.UpdatedAt))!
            .SetValue(collection, startUtc.AddHours(11));
    }
}
