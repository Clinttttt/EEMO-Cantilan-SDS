using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Fees;

/// <summary>
/// The daily fee for one market stall, where an office prices the areas of its market apart.
///
/// The office asked for this on 2026-08-23: Cantilan charges ₱30 across its market, but another LGU may charge ₱35 for
/// vegetables and ₱30 for fish, and the platform had one rate for a whole market. The rule now has four steps, and this
/// suite pins the order and — the part that matters most — that an office stating ONE market rate is answered exactly as
/// it was before the rule existed.
///
/// Phase 1: the rule and its resolution, with no billing path reading it yet. The per-area keys are deliberately absent
/// from FacilityRateKeys.For(NPM), so no screen offers one and activation refuses one, because a rate an office can set
/// and no collection honours is worse than not offering it at all.
/// </summary>
public class NpmDailyFeeTests
{
    private static readonly DateOnly Today = new(2026, 8, 23);
    private static readonly DateOnly Base = new(2020, 1, 1);

    private static FeeRateSnapshot Snapshot(params (FeeRateKey Key, decimal Amount, DateOnly From)[] rows) =>
        new(rows.Select(r => new FeeRateEntry(FacilityCode.NPM, r.Key, r.Amount, r.From)).ToList());

    private static Stall CanonicalStall(MarketSection section, decimal? ownRate = null)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", 0m, ApplicableFees.None, section: section, dailyRate: ownRate);
        return stall;
    }

    private static Stall OwnAreaStall(string area, decimal? ownRate) =>
        Stall.Create(Guid.NewGuid(), "1", 0m, ApplicableFees.None, dailyRate: ownRate, customSectionName: area);

    [Fact]
    public void AnOfficeWithONERateForItsMarketIsAnsweredThatRate_ForEveryArea()
    {
        // Cantilan. No per-area rows exist, so every area resolves the market rate, which is what every billing path
        // did before this rule and must keep doing.
        var snapshot = Snapshot((FeeRateKey.NpmDailyStall, 30m, Base));

        Assert.Equal(30m, NpmDailyFee.ForStall(CanonicalStall(MarketSection.VegetableArea), snapshot, Today));
        Assert.Equal(30m, NpmDailyFee.ForStall(CanonicalStall(MarketSection.FishSection), snapshot, Today));
        Assert.Equal(30m, NpmDailyFee.ForStall(CanonicalStall(MarketSection.MeatSection), snapshot, Today));
    }

    [Fact]
    public void AnAreaTheOfficePricesApartIsBilledItsOwnRate()
    {
        var snapshot = Snapshot(
            (FeeRateKey.NpmDailyStall, 30m, Base),
            (FeeRateKey.NpmDailyStallVegetable, 35m, Base));

        Assert.Equal(35m, NpmDailyFee.ForStall(CanonicalStall(MarketSection.VegetableArea), snapshot, Today));

        // And an area it did not price apart still takes the market's rate.
        Assert.Equal(30m, NpmDailyFee.ForStall(CanonicalStall(MarketSection.FishSection), snapshot, Today));
        Assert.Equal(30m, NpmDailyFee.ForStall(CanonicalStall(MarketSection.MeatSection), snapshot, Today));
    }

    [Fact]
    public void AnAreasRateCarriesItsOwnEffectiveDate_AndIsNeverRetroactive()
    {
        // The same guarantee the market's rate has: a raise applies from the day it was stated, and an elapsed day is
        // still answered with the figure it was billed at.
        var raised = new DateOnly(2026, 8, 1);
        var snapshot = Snapshot(
            (FeeRateKey.NpmDailyStall, 30m, Base),
            (FeeRateKey.NpmDailyStallVegetable, 35m, Base),
            (FeeRateKey.NpmDailyStallVegetable, 40m, raised));

        var stall = CanonicalStall(MarketSection.VegetableArea);

        Assert.Equal(35m, NpmDailyFee.ForStall(stall, snapshot, raised.AddDays(-1)));
        Assert.Equal(40m, NpmDailyFee.ForStall(stall, snapshot, raised));
        Assert.Equal(40m, NpmDailyFee.ForStall(stall, snapshot, Today));
    }

    [Fact]
    public void AStallOfTheMarketsOwnAreaKeepsTheRateItWasLetAt()
    {
        // Unchanged behaviour: a custom-section stall is let at its own rate, ahead of anything the ordinance says for
        // the market or for an area.
        var snapshot = Snapshot(
            (FeeRateKey.NpmDailyStall, 30m, Base),
            (FeeRateKey.NpmDailyStallVegetable, 35m, Base));

        Assert.Equal(45m, NpmDailyFee.ForStall(OwnAreaStall("Rice Section", 45m), snapshot, Today));

        // With no rate of its own it falls back to the market's, exactly as Stall.ResolveDailyFee always did.
        Assert.Equal(30m, NpmDailyFee.ForStall(OwnAreaStall("Rice Section", null), snapshot, Today));
    }

    [Fact]
    public void ACanonicalStallsOwnRateIsStillIgnored()
    {
        // An area's price belongs to the ordinance, not to a figure typed against one stall. This is what the platform
        // has always done, and widening it here would have re-priced stalls nobody asked about.
        var snapshot = Snapshot((FeeRateKey.NpmDailyStall, 30m, Base));

        Assert.Equal(30m, NpmDailyFee.ForStall(CanonicalStall(MarketSection.FishSection, ownRate: 99m), snapshot, Today));
    }

    [Fact]
    public void AnOfficeThatHasStatedNoDailyRateIsNotBilledOne()
    {
        var nothing = Snapshot();

        Assert.Null(NpmDailyFee.ForStallOrNull(CanonicalStall(MarketSection.VegetableArea), nothing, Today));
        Assert.Equal(0m, NpmDailyFee.ForStall(CanonicalStall(MarketSection.VegetableArea), nothing, Today));

        // An area priced apart is enough on its own: the office stated a rate for that area.
        var areaOnly = Snapshot((FeeRateKey.NpmDailyStallFish, 30m, Base));
        Assert.Equal(30m, NpmDailyFee.ForStall(CanonicalStall(MarketSection.FishSection), areaOnly, Today));
        Assert.Null(NpmDailyFee.ForStallOrNull(CanonicalStall(MarketSection.MeatSection), areaOnly, Today));
    }

    [Fact]
    public void AnAreasRateBelongsToTheMarketAndNowhereElse()
    {
        // A row filed against another facility is not this key's rate. The resolver already refuses one; this states it
        // for the new keys too.
        Assert.Equal(FacilityCode.NPM, FacilityRateKeys.OwnerOf(FeeRateKey.NpmDailyStallVegetable));
        Assert.Equal(FacilityCode.NPM, FacilityRateKeys.OwnerOf(FeeRateKey.NpmDailyStallFish));
        Assert.Equal(FacilityCode.NPM, FacilityRateKeys.OwnerOf(FeeRateKey.NpmDailyStallMeat));

        var misfiled = new FeeRateSnapshot(new[]
        {
            new FeeRateEntry(FacilityCode.SLH, FeeRateKey.NpmDailyStallVegetable, 999m, Base),
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, Base),
        });

        Assert.Equal(30m, NpmDailyFee.ForStall(CanonicalStall(MarketSection.VegetableArea), misfiled, Today));
    }

    [Fact]
    public void NoScreenOffersAnAreaRateUntilEveryBillingPathReadsOne()
    {
        // The guard on phase 1. FacilityRateKeys.For(NPM) is what the office's rate editor lists and what activation
        // accepts; a rate an office can set while collections still charge the market rate is a figure on screen that
        // nothing honours. These join that list in the same change that makes billing read them.
        var offered = FacilityRateKeys.For(FacilityCode.NPM);

        Assert.DoesNotContain(FeeRateKey.NpmDailyStallVegetable, offered);
        Assert.DoesNotContain(FeeRateKey.NpmDailyStallFish, offered);
        Assert.DoesNotContain(FeeRateKey.NpmDailyStallMeat, offered);

        // But they are recognised as per-area keys, so the change that wires them cannot miss one.
        Assert.True(FacilityRateKeys.IsPerAreaDailyKey(FeeRateKey.NpmDailyStallFish));
        Assert.False(FacilityRateKeys.IsPerAreaDailyKey(FeeRateKey.NpmDailyStall));
        Assert.Equal(3, Enum.GetValues<MarketSection>().Count(s => FacilityRateKeys.PerAreaDailyKey(s) is not null));
    }

    [Fact]
    public void AnAreasStatedRateIsReadableWithoutAStallInHand()
    {
        var snapshot = Snapshot(
            (FeeRateKey.NpmDailyStall, 30m, Base),
            (FeeRateKey.NpmDailyStallMeat, 50m, Base));

        Assert.Equal(50m, NpmDailyFee.ForAreaOrNull(MarketSection.MeatSection, snapshot, Today));
        Assert.Equal(30m, NpmDailyFee.ForAreaOrNull(MarketSection.FishSection, snapshot, Today));
        Assert.Null(NpmDailyFee.ForAreaOrNull(MarketSection.FishSection, Snapshot(), Today));
    }
}
