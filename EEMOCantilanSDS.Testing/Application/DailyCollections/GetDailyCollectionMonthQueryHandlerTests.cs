using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Queries.DailyCollections.GetDailyCollectionMonth;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class GetDailyCollectionMonthQueryHandlerTests
{
    [Fact]
    public async Task Handle_CountsMissedDaysFromContractStartForCompletedMonth()
    {
        var month = PhilippineTime.Today.AddMonths(-1);
        var contractDate = new DateOnly(month.Year, month.Month, 10);
        var stall = CreateNpmStallWithContract(contractDate);
        var paidDay10 = CreatePaidCollection(stall.Id, contractDate);
        var paidDay12 = CreatePaidCollection(stall.Id, contractDate.AddDays(2));

        var handler = CreateHandler(stall, [paidDay10, paidDay12]);

        var result = await handler.Handle(
            new GetDailyCollectionMonthQuery(stall.Id, month.Year, month.Month),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        var expectedValidDays = DateTime.DaysInMonth(month.Year, month.Month) - contractDate.Day + 1;
        Assert.Equal(expectedValidDays, dto.TotalDays);
        Assert.Equal(2, dto.DaysCollected);
        Assert.Equal(expectedValidDays - 2, dto.DaysMissed);
        Assert.Equal(2 * FeeRates.NpmDailyFee, dto.TotalDailyFee);
    }

    [Fact]
    public async Task Handle_CurrentMonth_DoesNotCountFutureDaysAsMissed()
    {
        var today = PhilippineTime.Today;
        var stall = CreateNpmStallWithContract(new DateOnly(today.Year, today.Month, 1));

        var handler = CreateHandler(stall, []);

        var result = await handler.Handle(
            new GetDailyCollectionMonthQuery(stall.Id, today.Year, today.Month),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.Equal(today.Day, dto.TotalDays);
        Assert.Equal(0, dto.DaysCollected);
        Assert.Equal(today.Day, dto.DaysMissed);
    }

    [Fact]
    public async Task Handle_AbsentDay_IsExcused_NotCountedAsMissed()
    {
        var month = PhilippineTime.Today.AddMonths(-1);
        var contractDate = new DateOnly(month.Year, month.Month, 1);
        var stall = CreateNpmStallWithContract(contractDate);
        var absentDate = new DateOnly(month.Year, month.Month, 5);
        var absent = DailyCollection.Create(stall.Id, absentDate);
        absent.MarkAbsent();

        var handler = CreateHandler(stall, [absent]);

        var result = await handler.Handle(
            new GetDailyCollectionMonthQuery(stall.Id, month.Year, month.Month),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        var key = absentDate.ToString("yyyy-MM-dd");
        Assert.True(dto.Collections[key].IsAbsent);
        Assert.False(dto.Collections[key].IsPaid);
        Assert.Equal(0, dto.DaysCollected);
        Assert.Equal(1, dto.DaysAbsent);
        // The excused day is removed from the missed count: a fully-elapsed month with N valid days and
        // one excused day has N−1 missed (not N), since nothing was owed on the absent day.
        Assert.Equal(dto.TotalDays - 1, dto.DaysMissed);
    }

    [Fact]
    public async Task Handle_MarketClosedDay_ReadsAsClosed_ExcusedNotMissed()
    {
        var month = PhilippineTime.Today.AddMonths(-1);
        var contractDate = new DateOnly(month.Year, month.Month, 1);
        var stall = CreateNpmStallWithContract(contractDate);
        var closedDate = new DateOnly(month.Year, month.Month, 23);
        var closure = NpmMarketClosure.Create(closedDate, MarketClosureReason.Holiday, "Market closed", "Head");

        // No per-stall record for the 23rd — the closure is facility-wide.
        var handler = CreateHandler(stall, [], [closure]);

        var result = await handler.Handle(
            new GetDailyCollectionMonthQuery(stall.Id, month.Year, month.Month),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        var key = closedDate.ToString("yyyy-MM-dd");
        Assert.True(dto.Collections[key].IsMarketClosed);   // shows as "Closed", not unpaid
        Assert.False(dto.Collections[key].IsPaid);
        Assert.False(dto.Collections[key].IsAbsent);
        Assert.Equal(1, dto.DaysClosed);
        Assert.Equal(0, dto.DaysCollected);
        // Closed day is excused — N valid days, 1 closed → N−1 missed (not N).
        Assert.Equal(dto.TotalDays - 1, dto.DaysMissed);
    }

    private static GetDailyCollectionMonthQueryHandler CreateHandler(
        Stall stall,
        IReadOnlyList<DailyCollection> collections,
        IReadOnlyList<NpmMarketClosure>? closures = null)
    {
        var dailyCollectionRepository = new Mock<IDailyCollectionRepository>();
        var stallRepository = new Mock<IStallRepository>();
        var marketClosureRepository = new Mock<INpmMarketClosureRepository>();

        stallRepository
            .Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stall);

        dailyCollectionRepository
            .Setup(r => r.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(collections);

        marketClosureRepository
            .Setup(r => r.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(closures ?? new List<NpmMarketClosure>());

        return new GetDailyCollectionMonthQueryHandler(
            dailyCollectionRepository.Object,
            stallRepository.Object,
            CacheTestDoubles.FeeRateResolver,
            marketClosureRepository.Object);
    }

    private static Stall CreateNpmStallWithContract(DateOnly contractDate)
    {
        var stall = Stall.Create(
            Guid.NewGuid(),
            "1",
            900m,
            ApplicableFees.DailyRental,
            section: MarketSection.VegetableArea,
            dailyRate: FeeRates.NpmDailyFee);

        // Market-closure handling is NPM-only, keyed off the stall's facility.
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));

        stall.Contracts.Add(Contract.Create(
            stall.Id,
            "Test Occupant",
            "Test Occupant",
            contractDate,
            3,
            900m));

        return stall;
    }

    private static DailyCollection CreatePaidCollection(Guid stallId, DateOnly collectionDate)
    {
        var collection = DailyCollection.Create(stallId, collectionDate);
        collection.MarkPaid(string.Empty, collectorId: null);
        return collection;
    }
}
