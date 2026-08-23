using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
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
        var repo = new Mock<ISlaughterRepository>();
        repo.Setup(r => r.GetOverviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Bare());

        var snapshot = new FeeRateSnapshot(stated
            .Select(s => new FeeRateEntry(FacilityCode.SLH, s.Key, s.Amount, new DateOnly(2020, 1, 1)))
            .ToList());

        var resolver = new Mock<IFeeRateResolver>();
        resolver.Setup(r => r.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);

        var result = await new GetSlaughterOverviewQueryHandler(repo.Object, resolver.Object)
            .Handle(new GetSlaughterOverviewQuery(2026, 8), default);

        Assert.True(result.IsSuccess);
        return result.Value!;
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
