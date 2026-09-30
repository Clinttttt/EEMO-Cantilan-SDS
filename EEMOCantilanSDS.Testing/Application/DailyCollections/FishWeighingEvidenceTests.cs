using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// New NPM Fish weighing rows freeze their rate, its effective date and the resulting amount at collection time
/// (IA-049), exactly as Meat does. Rows without that evidence stay unresolved and are never re-priced from a later rate.
/// </summary>
public sealed class FishWeighingEvidenceTests
{
    private static readonly DateOnly Day = new(2026, 9, 30);
    private static readonly DateOnly OldRateDate = new(2020, 1, 1);

    // ── Domain ────────────────────────────────────────────────────────────────────────────────────────

    private static DailyCollection Paid(decimal? kilos, decimal? rate, DateOnly? effective)
    {
        var collection = DailyCollection.Create(Guid.NewGuid(), Day);
        collection.MarkPaid("OR-1", null, kilos, "test", fishFeeRatePerKilo: rate, fishFeeRateEffectiveDate: effective);
        return collection;
    }

    [Fact]
    public void PaidFishWeighing_FreezesRateDateAndAmount()
    {
        var row = Paid(25m, 1.5m, OldRateDate);

        Assert.Equal(1.5m, row.FishFeeRatePerKilo);
        Assert.Equal(OldRateDate, row.FishFeeRateEffectiveDate);
        Assert.Equal(37.5m, row.FishFeeAmountFrozen);
        // The legacy read-time figure is untouched: existing reports read exactly as before.
        Assert.Equal(25m, row.FishFeeAmount);
    }

    [Fact]
    public void FishWeighingWithoutEvidence_StaysUnfrozen_NeverGivenAnAmount()
    {
        var row = Paid(25m, null, null);

        Assert.Null(row.FishFeeRatePerKilo);
        Assert.Null(row.FishFeeAmountFrozen);
    }

    [Theory]
    [InlineData(1.0, false, 5.0)]   // rate without its effective date
    [InlineData(0.0, true, 5.0)]    // a zero rate is not evidence
    [InlineData(1.0, true, 0.0)]    // evidence needs kilos
    public void IncompleteOrImpossibleFishEvidence_IsRefused(double rate, bool hasDate, double kilos)
    {
        var effective = hasDate ? OldRateDate : (DateOnly?)null;
        Assert.Throws<ArgumentException>(() => Paid((decimal)kilos, (decimal)rate, effective));
    }

    [Fact]
    public void FishRateCannotBeEffectiveAfterTheCollectionDate()
    {
        Assert.Throws<ArgumentException>(() => Paid(5m, 1m, Day.AddDays(1)));
    }

    [Fact]
    public void UnpayingOrExcusingADay_ClearsTheFrozenEvidence()
    {
        var row = Paid(25m, 1m, OldRateDate);
        row.MarkUnpaid("test");
        Assert.All(new object?[] { row.FishFeeRatePerKilo, row.FishFeeRateEffectiveDate, row.FishFeeAmountFrozen }, Assert.Null);

        var absent = Paid(25m, 1m, OldRateDate);
        absent.MarkAbsent("test");
        Assert.Null(absent.FishFeeAmountFrozen);
    }

    // ── Handler ───────────────────────────────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public Mock<IDailyCollectionRepository> Daily { get; } = new();
        public DailyCollection? Saved;
        public Stall Stall { get; }
        public IReadOnlyList<FeeRateEntry> Rates { get; set; }
        public DailyCollection? Existing { get; set; }

        public Harness(params FeeRateEntry[] rates)
        {
            Rates = rates;
            Stall = Stall.Create(Guid.NewGuid(), "F-1", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
            typeof(Stall).GetProperty(nameof(Stall.Facility))!
                .SetValue(Stall, Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        }

        public async Task<EEMOCantilanSDS.Application.Common.Result<bool>> RunAsync(RecordDailyCollectionCommand request)
        {
            var stalls = new Mock<IStallRepository>();
            stalls.Setup(x => x.GetByIdAsync(Stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Stall);
            Daily.Setup(x => x.GetByStallAndDateAsync(Stall.Id, request.CollectionDate, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Existing);
            Daily.Setup(x => x.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
                .Callback<DailyCollection, CancellationToken>((v, _) => Saved = v).Returns(Task.CompletedTask);
            var orNumbers = new Mock<IOrNumberRegistry>();
            orNumbers.Setup(o => o.IsAvailableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var uow = new Mock<IUnitOfWork>();
            uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var feeRates = new Mock<IFeeRateResolver>();
            feeRates.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FeeRateSnapshot(Rates));
            var handler = new RecordDailyCollectionCommandHandler(
                Daily.Object, Mock.Of<IPaymentRepository>(), orNumbers.Object, stalls.Object,
                Mock.Of<ICollectorRepository>(), Mock.Of<ICurrentUserService>(), uow.Object,
                CacheTestDoubles.Invalidator, feeRates.Object, CacheTestDoubles.Tenant);
            return await handler.Handle(request, CancellationToken.None);
        }
    }

    private static FeeRateEntry Daily30() => new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, OldRateDate);
    private static FeeRateEntry Fish(decimal rate, DateOnly effective) => new(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, rate, effective);

    [Fact]
    public async Task CollectingFish_FreezesTheRateInForceOnTheCollectionDate_NotALaterOne()
    {
        var h = new Harness(Daily30(), Fish(1m, OldRateDate), Fish(2m, Day.AddDays(10)));

        var result = await h.RunAsync(new RecordDailyCollectionCommand(h.Stall.Id, Day, IsPaid: true, FishKilos: 10m));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1m, h.Saved!.FishFeeRatePerKilo);
        Assert.Equal(OldRateDate, h.Saved.FishFeeRateEffectiveDate);
        Assert.Equal(10m, h.Saved.FishFeeAmountFrozen);
    }

    [Fact]
    public async Task AnOfficeThatStatedNoFishRate_StillCollects_ButFreezesNothing()
    {
        var h = new Harness(Daily30());

        var result = await h.RunAsync(new RecordDailyCollectionCommand(h.Stall.Id, Day, IsPaid: true, FishKilos: 10m));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(10m, h.Saved!.FishKilos);
        Assert.Null(h.Saved.FishFeeRatePerKilo);
        Assert.Null(h.Saved.FishFeeAmountFrozen); // unresolved, never an invented amount
    }

    [Fact]
    public async Task ReMarkingWithUnchangedKilos_KeepsTheOriginalEvidence_WhenTheRateHasSinceChanged()
    {
        var existing = DailyCollection.Create(Guid.NewGuid(), Day, dailyFee: 30m);
        var h = new Harness(Daily30(), Fish(1m, OldRateDate), Fish(5m, Day)); // the rate changed ON the day, after the first mark
        existing.MarkPaid("OR-1", null, 10m, "test", fishFeeRatePerKilo: 1m, fishFeeRateEffectiveDate: OldRateDate);
        h.Existing = existing;

        var result = await h.RunAsync(new RecordDailyCollectionCommand(h.Stall.Id, Day, IsPaid: true, FishKilos: 10m, ORNumber: "OR-1"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(1m, existing.FishFeeRatePerKilo);
        Assert.Equal(10m, existing.FishFeeAmountFrozen);
    }

    [Fact]
    public async Task ChangedKilos_ResolveTheEvidenceAgainForThatCorrection()
    {
        var existing = DailyCollection.Create(Guid.NewGuid(), Day, dailyFee: 30m);
        existing.MarkPaid("OR-1", null, 10m, "test", fishFeeRatePerKilo: 1m, fishFeeRateEffectiveDate: OldRateDate);
        var h = new Harness(Daily30(), Fish(1m, OldRateDate)) { Existing = existing };

        var result = await h.RunAsync(new RecordDailyCollectionCommand(h.Stall.Id, Day, IsPaid: true, FishKilos: 12m, ORNumber: "OR-1"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(12m, existing.FishKilos);
        Assert.Equal(12m, existing.FishFeeAmountFrozen);
    }
}
