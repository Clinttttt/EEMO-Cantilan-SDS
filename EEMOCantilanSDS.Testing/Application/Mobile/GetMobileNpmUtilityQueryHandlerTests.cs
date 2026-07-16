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
    private static GetMobileNpmUtilityQueryHandler Build(CollectorUser? collector, string? role, Guid? collectorId)
    {
        var util = new Mock<IUtilityBillRepository>();
        util.Setup(r => r.GetForMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UtilityBill>());

        var stalls = new Mock<IStallRepository>();
        stalls.Setup(r => r.GetStallsByFacilityAsync(It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StallDto>());

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
}
