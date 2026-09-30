using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Testing;

public sealed class DailyCollectionMeatWeighingTests
{
    private static readonly DateOnly RateEffective = new(2026, 9, 29);

    [Fact]
    public void Paid_meat_source_freezes_quantity_rate_effectivity_and_amount()
    {
        var collection = DailyCollection.Create(Guid.NewGuid(), RateEffective, dailyFee: 30m);

        collection.MarkPaid("", null, updatedBy: "collector",
            meatKilos: 2.5m, meatFeeRatePerKilo: 66m, meatFeeRateEffectiveDate: RateEffective);

        Assert.Equal(2.5m, collection.MeatKilos);
        Assert.Equal(66m, collection.MeatFeeRatePerKilo);
        Assert.Equal(RateEffective, collection.MeatFeeRateEffectiveDate);
        Assert.Equal(165m, collection.MeatFeeAmount);
        Assert.Equal(195m, collection.TotalCollected);
    }

    [Fact]
    public void Meat_source_rejects_missing_or_future_rate_evidence()
    {
        var collection = DailyCollection.Create(Guid.NewGuid(), RateEffective);

        Assert.Throws<ArgumentException>(() => collection.MarkPaid("", null,
            updatedBy: "collector", meatKilos: 1m));
        Assert.Throws<ArgumentException>(() => collection.MarkPaid("", null,
            updatedBy: "collector", meatKilos: 1m, meatFeeRatePerKilo: 66m,
            meatFeeRateEffectiveDate: RateEffective.AddDays(1)));
    }

    [Fact]
    public void Unpaid_or_absent_transition_clears_meat_source_facts()
    {
        var collection = DailyCollection.Create(Guid.NewGuid(), RateEffective);
        collection.MarkPaid("", null, updatedBy: "collector",
            meatKilos: 1m, meatFeeRatePerKilo: 66m, meatFeeRateEffectiveDate: RateEffective);

        collection.MarkUnpaid("office");

        Assert.Null(collection.MeatKilos);
        Assert.Null(collection.MeatFeeRatePerKilo);
        Assert.Null(collection.MeatFeeRateEffectiveDate);
        Assert.Equal(0m, collection.MeatFeeAmount);
    }
}
