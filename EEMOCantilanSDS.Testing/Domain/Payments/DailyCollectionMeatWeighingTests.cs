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
    public void Canonical_rent_void_preserves_independent_weighing_evidence_and_recognition_date()
    {
        var collector = Guid.NewGuid();
        var day = DailyCollection.Create(Guid.NewGuid(), RateEffective, dailyFee: 30m);
        day.MarkPaid("", collector, fishKilos: 5m, fishFeeRatePerKilo: 1m,
            fishFeeRateEffectiveDate: RateEffective, meatKilos: 2m,
            meatFeeRatePerKilo: 66m, meatFeeRateEffectiveDate: RateEffective);
        day.ApplyCanonicalPayment(Guid.NewGuid());
        var recordedAt = day.UpdatedAt;

        day.ApplyCanonicalVoid("office");

        Assert.False(day.IsPaid);
        Assert.Null(day.CanonicalCollectionId);
        Assert.Equal(collector, day.CollectorId);
        Assert.Equal(5m, day.FishFeeAmountFrozen);
        Assert.Equal(132m, day.MeatFeeAmount);
        Assert.Equal(recordedAt, day.UpdatedAt);

        day.MarkPaid("", Guid.NewGuid());
        day.ApplyCanonicalPayment(Guid.NewGuid());
        Assert.True(day.IsPaid);
        Assert.Equal(collector, day.CollectorId);
        Assert.Equal(5m, day.FishFeeAmountFrozen);
        Assert.Equal(132m, day.MeatFeeAmount);
        Assert.Equal(recordedAt, day.UpdatedAt);
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
