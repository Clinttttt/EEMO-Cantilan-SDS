using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Queries.Collectors.GetReportOfCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class CollectorReportMonthEndAdjustmentTests : RepositoryTestBase
{
    [Fact]
    public async Task ReportOfCollections_CountsAdjustedDailyFeeOnce_AndKeepsFishAndNormalRows()
    {
        await using var context = NewContext();
        var today = PhilippineTime.Today;
        var collector = CollectorUser.Create("Report Collector", "EEMO-2026-011", "report-collector", "report@x.com", "0917", TestPasswords.Hash("pw"));
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var normalStall = Stall.Create(facility.Id, "N-1", 900m, ApplicableFees.DailyRental | ApplicableFees.FishFee, section: MarketSection.FishSection);
        var adjustedStall = Stall.Create(facility.Id, "N-2", 900m, ApplicableFees.DailyRental | ApplicableFees.FishFee, section: MarketSection.FishSection);
        var normalContract = Contract.Create(normalStall.Id, "Normal", "Normal", new DateOnly(2020, 1, 1), 20, 900m);
        var adjustedContract = Contract.Create(adjustedStall.Id, "Adjusted", "Adjusted", new DateOnly(2020, 1, 1), 20, 900m);
        var recordedAt = PhilippineTime.DayUtcRange(today).StartUtc.AddHours(1);

        var normal = DailyCollection.Create(normalStall.Id, today);
        normal.MarkPaid("OR-NORMAL", collector.Id, fishKilos: 2m);
        normal.CreatedAt = recordedAt;
        normal.UpdatedAt = recordedAt;

        var adjusted = DailyCollection.Create(adjustedStall.Id, today, dailyFee: 40m);
        adjusted.MarkPaid("OR-ADJUSTED", collector.Id, fishKilos: 3m);
        adjusted.AddMonthEndAdjustment(60m);
        adjusted.CreatedAt = recordedAt;
        adjusted.UpdatedAt = recordedAt;

        context.AddRange(collector, facility, normalStall, adjustedStall, normalContract, adjustedContract, normal, adjusted);
        await context.SaveChangesAsync();

        var collectors = new Mock<ICollectorRepository>();
        collectors.Setup(repository => repository.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(collector);
        var handler = new GetReportOfCollectionsQueryHandler(collectors.Object, new CollectorReportQueries(context));

        var result = await handler.Handle(
            new GetReportOfCollectionsQuery(collector.Id, today, today), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var report = result.Value!;
        // The test office's fish rate is ₱1/kg: normal ₱30 + ₱2 fish; adjusted DailyFee ₱100 + ₱3 fish.
        Assert.Equal(135m, report.TotalCollected);
        Assert.Equal(135m, Assert.Single(report.Facilities).Amount);
        Assert.Equal(135m, Assert.Single(report.Days).Amount);
        Assert.Equal(2, report.ReceiptsIssued);
        Assert.Equal(32m, Assert.Single(report.Receipts, receipt => receipt.OrNumber == "OR-NORMAL").Amount);
        Assert.Equal(103m, Assert.Single(report.Receipts, receipt => receipt.OrNumber == "OR-ADJUSTED").Amount);
    }
}
