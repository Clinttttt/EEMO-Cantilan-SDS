using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Queries.Collections.GetCollectionActivity;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>
/// The office Collection Activity handler: Head/Admin of the resolved tenant only, a bounded period, and totals that cover
/// every event in the period even when the list is cut to the limit. Exactly-once composition is proven on PostgreSQL in
/// CollectionActivityExactlyOnceTests.
/// </summary>
public class GetCollectionActivityQueryHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 1);

    private static CollectionActivityEventDto Event(string key, string authority, decimal amount, decimal effect = 0m,
        FacilityCode? facility = null, Guid? collector = null, int minutes = 0) =>
        new(key, authority, authority == "Canonical" ? "Collection" : "PaymentRecord", null, Day,
            new DateTime(2026, 10, 1, 1, minutes, 0, DateTimeKind.Utc), authority == "Canonical" ? "SRC-2026-000001" : null, null, null, [], [], null, null, collector, null, "office",
            facility, null, null, amount, effect, amount + effect, "Posted", null, [], []);

    private static (GetCollectionActivityQueryHandler Handler, Mock<ICollectionActivityReader> Reader) Build(
        string role = "Admin", Guid? claimed = null, params CollectionActivityEventDto[] events)
    {
        var reader = new Mock<ICollectionActivityReader>();
        reader.Setup(r => r.GetAsync(Tenant, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(events);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.IsAuthenticated).Returns(true);
        user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        user.SetupGet(u => u.Role).Returns(role);
        user.SetupGet(u => u.MunicipalityId).Returns(claimed ?? Tenant);
        var tenant = new Mock<ICurrentMunicipalityAccessor>();
        tenant.SetupGet(t => t.MunicipalityId).Returns(Tenant);
        return (new GetCollectionActivityQueryHandler(reader.Object, user.Object, tenant.Object, new FixedClock(DateTime.UtcNow)), reader);
    }

    [Theory]
    [InlineData("Collector")]
    [InlineData("Payor")]
    public async Task OnlyOfficeStaffReadIt(string role)
    {
        var (handler, reader) = Build(role);

        var result = await handler.Handle(new GetCollectionActivityQuery(Day, Day), default);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        reader.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AClaimForAnotherTenant_IsRefused_BeforeAnyRead()
    {
        var (handler, reader) = Build(claimed: Guid.NewGuid());

        Assert.Equal(ResultStatus.Forbidden, (await handler.Handle(new GetCollectionActivityQuery(Day, Day), default)).Status);
        reader.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(1, 0)]   // to before from
    [InlineData(0, 31)]  // 32 days
    public async Task AnInvalidPeriod_IsRefused(int fromOffset, int toOffset)
    {
        var (handler, _) = Build();

        var result = await handler.Handle(new GetCollectionActivityQuery(Day.AddDays(fromOffset), Day.AddDays(toOffset)), default);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task AThirtyOneDayPeriod_IsAccepted()
    {
        var (handler, _) = Build();

        Assert.True((await handler.Handle(new GetCollectionActivityQuery(Day, Day.AddDays(30)), default)).IsSuccess);
    }

    [Fact]
    public async Task TotalsCoverEveryEvent_EvenWhenTheListIsCutToTheLimit()
    {
        var (handler, _) = Build(events:
        [
            Event("PaymentRecord:1", "Legacy", 900m, minutes: 1),
            Event("Collection:2", "Canonical", 250m, minutes: 2),
            Event("Collection:3", "Canonical", 30m, effect: -30m, minutes: 3)
        ]);

        var feed = (await handler.Handle(new GetCollectionActivityQuery(Day, Day, Limit: 2), default)).Value!;

        Assert.True(feed.Truncated);
        Assert.Equal(3, feed.EventCount);
        Assert.Equal(new[] { "Collection:3", "Collection:2" }, feed.Events.Select(e => e.EventKey));
        Assert.Equal((900m, 280m, -30m, 1150m), (feed.LegacyAmount, feed.CanonicalAmount, feed.CorrectionEffect, feed.NetAmount));
        Assert.Equal("LatestCorrected", feed.CorrectionBasis);
    }

    [Fact]
    public async Task Filters_NarrowEventsAndTotals()
    {
        var collector = Guid.NewGuid();
        var (handler, _) = Build(events:
        [
            Event("PaymentRecord:1", "Legacy", 900m, facility: FacilityCode.TCC, collector: collector),
            Event("Collection:2", "Canonical", 250m, facility: FacilityCode.ICE),
            Event("Collection:3", "Canonical", 30m, collector: collector)
        ]);

        var byFacility = (await handler.Handle(new GetCollectionActivityQuery(Day, Day, Facility: FacilityCode.ICE), default)).Value!;
        var byCollector = (await handler.Handle(new GetCollectionActivityQuery(Day, Day, CollectorId: collector), default)).Value!;
        var legacy = (await handler.Handle(new GetCollectionActivityQuery(Day, Day, Authority: "Legacy"), default)).Value!;

        Assert.Equal(250m, byFacility.NetAmount);
        Assert.Equal((900m, 30m), (byCollector.LegacyAmount, byCollector.CanonicalAmount));
        Assert.Equal((1, 900m), (legacy.EventCount, legacy.NetAmount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5001)]
    public async Task AnOutOfRangeLimit_OrUnknownAuthority_IsRefused(int limit)
    {
        var (handler, _) = Build();

        Assert.Equal(ResultStatus.Invalid, (await handler.Handle(new GetCollectionActivityQuery(Day, Day, Limit: limit), default)).Status);
        Assert.Equal(ResultStatus.Invalid, (await handler.Handle(new GetCollectionActivityQuery(Day, Day, Authority: "Shadow"), default)).Status);
    }
}
