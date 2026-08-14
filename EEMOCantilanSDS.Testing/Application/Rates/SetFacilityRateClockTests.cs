using EEMOCantilanSDS.Application.Command.Rates.SetFacilityRate;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A command that stamps a date writes it into the ledger permanently, so the date it stamps has to be the office's, and it
/// has to be checkable.
///
/// <para>
/// A rate is effective from today FORWARD and never retroactively — elapsed periods stay exactly as they were billed, because
/// re-rating a month the office has already collected would silently disagree with the receipts it issued. That "today" is
/// now stated rather than taken from whatever machine ran the command.
/// </para>
/// </summary>
public class SetFacilityRateClockTests : RepositoryTestBase
{
    [Theory]
    [InlineData(2026, 3, 10)]
    [InlineData(2026, 12, 31)]
    [InlineData(2027, 1, 1)]
    public async Task ANewRateTakesEffectOnTheDayItWasSet(int year, int month, int day)
    {
        var today = new DateOnly(year, month, day);
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        context.Add(facility);
        await context.SaveChangesAsync();

        IClock clock = new FixedClock(today.ToDateTime(TimeOnly.MinValue).AddHours(-8));

        var result = await new SetFacilityRateCommandHandler(
                context, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, clock)
            .Handle(new SetFacilityRateCommand(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var rate = await context.FacilityRates.AsNoTracking().SingleAsync(r => r.RateKey == FeeRateKey.NpmDailyStall);
        Assert.Equal(today, rate.EffectiveDate);
    }

    [Fact]
    public async Task TheRateIsNotBackdatedIntoAlreadyBilledPeriods()
    {
        // The property that matters to the office: whatever day the rate is set, it never reaches back before that day. A
        // retroactive rate would restate months whose receipts have already been issued.
        var today = new DateOnly(2026, 6, 15);
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        context.Add(facility);
        await context.SaveChangesAsync();

        IClock clock = new FixedClock(today.ToDateTime(TimeOnly.MinValue).AddHours(-8));

        await new SetFacilityRateCommandHandler(context, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, clock)
            .Handle(new SetFacilityRateCommand(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m), CancellationToken.None);

        var rate = await context.FacilityRates.AsNoTracking().SingleAsync(r => r.RateKey == FeeRateKey.NpmDailyStall);

        Assert.False(rate.EffectiveDate < today, "a rate must never take effect before the day it was set");
        Assert.Equal(new DateOnly(2026, 6, 15), rate.EffectiveDate);
    }
}
