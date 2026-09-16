using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterOverview;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Slaughterhouse;

/// <summary>
/// The slaughterhouse overview states the OFFICE's per-head rates, and says nothing where its ordinance is silent.
///
/// It used to resolve them with <c>Resolve()</c>, which reads an unstated rate as zero, and the DTO defaulted to
/// Cantilan's ₱250 and ₱365. Both were visible: an office that does not slaughter carabao was offered a carabao at ₱0 a
/// head, and one whose rates had not loaded was quoted the reference municipality's ordinance. Null means unstated, and
/// the screen leaves that animal out.
/// </summary>
public class SlaughterOverviewRatesTests
{
    private static SlaughterOverviewDto Bare() => new(
        TotalTransactions: 0, TotalHeads: 0, TotalCollected: 0m,
        HogCount: 0, CarabaoCount: 0, CowCount: 0, OthersCount: 0);

    private static async Task<SlaughterOverviewDto> OverviewWith(params (FeeRateKey Key, decimal Amount)[] stated)
    {
        var snapshot = new FeeRateSnapshot(stated
            .Select(s => new FeeRateEntry(FacilityCode.SLH, s.Key, s.Amount, new DateOnly(2020, 1, 1)))
            .ToList());

        return await OverviewOn(2026, 8, new DateOnly(2026, 8, 20), snapshot);
    }

    /// <summary>The overview for one month, read on a given day, over a snapshot the caller states in full.</summary>
    private static async Task<SlaughterOverviewDto> OverviewOn(int year, int month, DateOnly today, FeeRateSnapshot snapshot)
    {
        var repo = new Mock<ISlaughterRepository>();
        repo.Setup(r => r.GetOverviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Bare());

        var resolver = new Mock<IFeeRateResolver>();
        resolver.Setup(r => r.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.PhilippineToday).Returns(today);

        var result = await new GetSlaughterOverviewQueryHandler(repo.Object, resolver.Object, clock.Object)
            .Handle(new GetSlaughterOverviewQuery(year, month), default);

        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task ARateRaisedMidMonth_IsTheRateTheScreenQuotesForTheRestOfIt()
    {
        // The defect this replaced: a rate edit takes effect the day it is made and is never retroactive, and the screen
        // asked for the rate as of the FIRST of the month. The office raised the hog fee from ₱250 to ₱251 on the 15th of
        // September and was still shown ₱250 on the 16th — while recording a transaction that same day charged ₱251,
        // because the recording handler resolves at the transaction date. A screen that quotes a fee must quote the fee
        // the transaction it is about to record will carry.
        var snapshot = new FeeRateSnapshot(new[]
        {
            new FeeRateEntry(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 250m, new DateOnly(2020, 1, 1)),
            new FeeRateEntry(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 251m, new DateOnly(2026, 9, 15))
        });

        Assert.Equal(250m, (await OverviewOn(2026, 9, new DateOnly(2026, 9, 14), snapshot)).HogRatePerHead);
        Assert.Equal(251m, (await OverviewOn(2026, 9, new DateOnly(2026, 9, 15), snapshot)).HogRatePerHead);
        Assert.Equal(251m, (await OverviewOn(2026, 9, new DateOnly(2026, 9, 16), snapshot)).HogRatePerHead);

        // A month already closed states the rate in force when it closed, not today's.
        Assert.Equal(251m, (await OverviewOn(2026, 9, new DateOnly(2026, 11, 3), snapshot)).HogRatePerHead);
        Assert.Equal(250m, (await OverviewOn(2026, 8, new DateOnly(2026, 11, 3), snapshot)).HogRatePerHead);

        // And a month not yet begun states what is in force at its start.
        Assert.Equal(250m, (await OverviewOn(2026, 9, new DateOnly(2026, 8, 2), snapshot)).HogRatePerHead);
    }

    [Fact]
    public async Task AStatedRateIsTheOfficesOwn()
    {
        var overview = await OverviewWith((FeeRateKey.SlhHogPerHead, 400m), (FeeRateKey.SlhLargePerHead, 500m));

        Assert.Equal(400m, overview.HogRatePerHead);
        Assert.Equal(500m, overview.LargeRatePerHead);
    }

    [Fact]
    public async Task AnUnstatedRateIsNull_NotZeroAndNotTheReferenceOrdinance()
    {
        var overview = await OverviewWith((FeeRateKey.SlhHogPerHead, 400m));

        Assert.Equal(400m, overview.HogRatePerHead);
        Assert.Null(overview.LargeRatePerHead);
    }

    [Fact]
    public async Task AnOfficeWithNothingStatedCarriesNoRates()
    {
        var overview = await OverviewWith();

        Assert.Null(overview.HogRatePerHead);
        Assert.Null(overview.LargeRatePerHead);
    }
}
