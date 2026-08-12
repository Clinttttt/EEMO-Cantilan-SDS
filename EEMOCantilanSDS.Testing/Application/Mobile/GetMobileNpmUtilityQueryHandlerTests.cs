using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileNpmUtility;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Audit finding #2 — the mobile NPM utility READ must enforce the same NPM facility-assignment guard as
/// the write path, so a collector not assigned to NPM cannot read another facility's utility records
/// inside the same LGU. Admins/heads are unrestricted.
/// </summary>
public class GetMobileNpmUtilityQueryHandlerTests
{
    private static GetMobileNpmUtilityQueryHandler Build(CollectorUser? collector, string? role, Guid? collectorId,
                                                         IReadOnlyList<UtilityBill>? bills = null,
                                                         IReadOnlyList<StallDto>? stallRows = null)
    {
        var util = new Mock<IUtilityBillRepository>();
        util.Setup(r => r.GetForMonthWithOutstandingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bills ?? Array.Empty<UtilityBill>());

        var stalls = new Mock<IStallRegisterQueries>();
        stalls.Setup(r => r.GetStallsByFacilityAsync(It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stallRows ?? Array.Empty<StallDto>());

        var facilities = new Mock<IFacilityRepository>();
        facilities.Setup(r => r.GetByCodeAsync(It.IsAny<FacilityCode>(), It.IsAny<CancellationToken>())).ReturnsAsync((Facility?)null);

        var collectors = new Mock<ICollectorRepository>();
        if (collector is not null)
            collectors.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(u => u.Role).Returns(role);
        currentUser.SetupGet(u => u.CollectorId).Returns(collectorId);

        return new GetMobileNpmUtilityQueryHandler(util.Object, stalls.Object, facilities.Object, collectors.Object, currentUser.Object);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var c = CollectorUser.Create("Test Collector", "EEMO-1", "tc", "tc@x.gov", "09170000000", "Passw0rd!");
        foreach (var code in codes)
            c.FacilityAssignments.Add(CollectorFacilityAssignment.Create(c.Id, Guid.NewGuid(), code));
        return c;
    }

    [Fact]
    public async Task Collector_NotAssignedToNpm_IsForbidden()
    {
        var collector = CollectorWith(FacilityCode.TCC);   // assigned elsewhere, not NPM
        var handler = Build(collector, "Collector", collector.Id);

        var result = await handler.Handle(new GetMobileNpmUtilityQuery(2026, 7), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Collector_AssignedToNpm_Succeeds()
    {
        var collector = CollectorWith(FacilityCode.NPM);
        var handler = Build(collector, "Collector", collector.Id);

        var result = await handler.Handle(new GetMobileNpmUtilityQuery(2026, 7), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Collector_WithNoCollectorId_IsForbidden()
    {
        var handler = Build(collector: null, "Collector", collectorId: null);

        var result = await handler.Handle(new GetMobileNpmUtilityQuery(2026, 7), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    /// <summary>
    /// The field app must be told which month each bill answers for, and an unpaid bill from an earlier month
    /// must come through — that is what the office hit: a recorded bill the collector could not see or name.
    /// </summary>
    [Fact]
    public async Task EachBill_StatesItsBillingPeriod_AndArrearsComeFirst()
    {
        var stallId = Guid.NewGuid();
        var july = UtilityBill.Create(stallId, 2026, 7, 0m, 0m, 0m, 0m, 56m, 1m);      // water only, ₱56 owed
        var august = UtilityBill.Create(stallId, 2026, 8, 0m, 0m, 0m, 0m, 60m, 1m);
        var stall = new StallDto(stallId, "3", StallStatus.Active, "Dante Revilla", null, null, null,
                                 900m, 30m, null, MarketSection.VegetableArea, null, null, null, 3, null, false, true);

        var handler = Build(collector: null, role: "Head", collectorId: null,
                            bills: new[] { august, july }, stallRows: new[] { stall });

        var result = await handler.Handle(new GetMobileNpmUtilityQuery(2026, 8), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rows = result.Value!.Bills;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("3", r.StallNo));
        // Oldest owed first, each naming its own month.
        Assert.Equal("July 2026", rows[0].PeriodLabel);
        Assert.Equal(7, rows[0].BillingMonth);
        Assert.Equal(56m, rows[0].WaterCharge);
        Assert.Equal(0m, rows[0].ElecCharge);          // a water-only bill charges no electricity
        Assert.Equal("August 2026", rows[1].PeriodLabel);
    }
}
