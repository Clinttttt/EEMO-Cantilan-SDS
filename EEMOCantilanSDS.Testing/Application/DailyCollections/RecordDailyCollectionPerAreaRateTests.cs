using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.DailyCollections;

/// <summary>
/// The money actually follows the area. NpmDailyFee states the rule; this bills through a real path with it, because a
/// rule that resolves correctly and is read by nothing charges nobody anything.
///
/// The office asked for per-area rates on 2026-08-23. Phase 2 routes every daily-fee read through that rule; this suite
/// stands over the one path where a peso is stamped onto a record — a daily collection — and proves three things: an
/// area the office prices apart is stamped at ITS fee, an area it prices no differently is stamped at the market's, and
/// a stall whose area carries no stated fee is refused rather than filed at zero.
/// </summary>
public class RecordDailyCollectionPerAreaRateTests
{
    private static readonly DateOnly Effective = new(2020, 1, 1);

    private sealed class FixedRates(params FeeRateEntry[] rows) : IFeeRateResolver
    {
        public Task<FeeRateSnapshot> GetSnapshotAsync(CancellationToken ct = default)
            => Task.FromResult(new FeeRateSnapshot(rows));
    }

    private static async Task<(bool Ok, string? Error, DailyCollection? Written)> RecordAsync(
        MarketSection section, IFeeRateResolver rates)
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental, section: section);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        var orNumbers = new Mock<IOrNumberRegistry>();

        orNumbers.Setup(o => o.IsAvailableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        currentUser.SetupGet(c => c.CollectorId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.Username).Returns("collector1");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        DailyCollection? captured = null;
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured = dc)
            .Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, orNumbers.Object, stallRepo.Object, collectorRepo.Object,
            currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, rates, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, DateOnly.FromDateTime(DateTime.UtcNow), IsPaid: true),
            CancellationToken.None);

        return (result.IsSuccess, result.Error, captured);
    }

    [Fact]
    public async Task AnAreaTheOfficePricesApartIsCollectedAtItsOwnFee()
    {
        var rates = new FixedRates(
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, Effective),
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStallVegetable, 35m, Effective));

        var (ok, error, written) = await RecordAsync(MarketSection.VegetableArea, rates);

        Assert.True(ok, error);
        Assert.NotNull(written);
        Assert.Equal(35m, written!.DailyFee);
    }

    [Fact]
    public async Task AnAreaPricedNoDifferentlyIsCollectedAtTheMarketsFee()
    {
        var rates = new FixedRates(
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, Effective),
            new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStallVegetable, 35m, Effective));

        var (ok, error, written) = await RecordAsync(MarketSection.FishSection, rates);

        Assert.True(ok, error);
        Assert.Equal(30m, written!.DailyFee);
    }

    [Fact]
    public async Task AnOfficeWithOneMarketRateIsUnchanged()
    {
        // The reference case: Cantilan states ₱30 for its market and nothing per area, so every area collects ₱30 —
        // which is what this path did before the rule existed.
        var rates = new FixedRates(new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, Effective));

        foreach (var section in new[] { MarketSection.VegetableArea, MarketSection.FishSection, MarketSection.MeatSection })
        {
            var (ok, error, written) = await RecordAsync(section, rates);
            Assert.True(ok, error);
            Assert.Equal(30m, written!.DailyFee);
        }
    }

    [Fact]
    public async Task AStallWhoseAreaHasNoStatedFeeIsRefused_NotCollectedAtZero()
    {
        // The office states a rate for its fish section and nothing else. A vegetable stall has no fee to be collected
        // at, and a day filed at ₱0 would reconcile against no ordinance at all.
        var rates = new FixedRates(new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStallFish, 30m, Effective));

        var (ok, error, written) = await RecordAsync(MarketSection.VegetableArea, rates);

        Assert.False(ok);
        Assert.Null(written);
        Assert.Contains("Daily stall fee", error);

        // And the fish stall it DID price is collected.
        var (fishOk, fishError, fishWritten) = await RecordAsync(MarketSection.FishSection, rates);
        Assert.True(fishOk, fishError);
        Assert.Equal(30m, fishWritten!.DailyFee);
    }
}
