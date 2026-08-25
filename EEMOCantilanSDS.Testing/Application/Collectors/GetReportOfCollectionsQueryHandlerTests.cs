using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Queries.Collectors.GetReportOfCollections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Collectors;

/// <summary>
/// The Report of Collections shapes a document from the receipt-level record. Three of its derivations could mislead an
/// office quietly, and each is pinned here.
///
/// <para>
/// A range of receipt numbers is only stated where the numbers actually run unbroken, since they are typed one by one and a
/// document must not claim a booklet series it cannot prove. The daily record says how much of a day's money answered for
/// EARLIER days, without which a day appears to collect more than it could possibly owe. And what the office itself
/// recorded stays out of the collector's totals, because it is not their accountability.
/// </para>
/// </summary>
public class GetReportOfCollectionsQueryHandlerTests
{
    private static readonly Guid Collector = Guid.NewGuid();
    private static readonly DateOnly Aug1 = new(2026, 8, 1);
    private static readonly DateOnly Aug24 = new(2026, 8, 24);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);

    [Fact]
    public async Task StatesARangeOnlyWhereTheReceiptNumbersRunUnbroken()
    {
        var unbroken = await Run(new[]
        {
            Line("1616460", Aug24, 30m),
            Line("1616461", Aug24, 30m),
            Line("1616462", Aug24, 30m)
        });
        Assert.Equal("1616460 to 1616462", Assert.Single(unbroken.Days).ReceiptSpan);

        var withAGap = await Run(new[]
        {
            Line("1616460", Aug24, 30m),
            Line("1616462", Aug24, 30m)
        });
        Assert.Equal("2 receipts", Assert.Single(withAGap.Days).ReceiptSpan);

        var notNumbers = await Run(new[]
        {
            Line("OR-A", Aug24, 30m),
            Line("OR-B", Aug24, 30m)
        });
        Assert.Equal("2 receipts", Assert.Single(notNumbers.Days).ReceiptSpan);
    }

    [Fact]
    public async Task SaysHowMuchOfADaysMoneyAnsweredForEarlierDays()
    {
        // One receipt cleared three owed days on the twenty fourth: sixty pesos of the ninety answered for earlier days.
        var report = await Run(new[]
        {
            Line("5656565", Aug24, 30m, feeDay: new DateOnly(2026, 8, 22)),
            Line("5656565", Aug24, 30m, feeDay: new DateOnly(2026, 8, 23)),
            Line("5656565", Aug24, 30m, feeDay: Aug24)
        });

        var day = Assert.Single(report.Days);
        Assert.Equal(90m, day.Amount);
        Assert.Equal(60m, day.ForEarlierDays);

        // And the receipt reads as one line naming the span it covered.
        var receipt = Assert.Single(report.Receipts);
        Assert.Equal(90m, receipt.Amount);
        Assert.Equal("Aug 22 to Aug 24, 2026 (3 days)", receipt.FeeFor);
        Assert.Equal(1, report.ReceiptsIssued);
    }

    [Fact]
    public async Task WhatTheOfficeRecordedStaysOutOfTheCollectorsTotals()
    {
        var report = await Run(
            new[] { Line("1616460", Aug24, 30m, feeDay: Aug24) },
            officeRecorded: 60m,
            officeReceipts: 2);

        Assert.Equal(30m, report.TotalCollected);      // the collector's own accountability
        Assert.Equal(60m, report.OfficeRecorded);      // stated, so the facility total reconciles
        Assert.Equal(2, report.OfficeReceipts);
    }

    [Fact]
    public async Task APeriodThatEndsBeforeItBeginsIsRefused()
    {
        var handler = Handler(Array.Empty<CollectorCollectionLine>());
        var result = await handler.Handle(new GetReportOfCollectionsQuery(Collector, Aug31, Aug1), CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    // ── fixtures ──

    private static CollectorCollectionLine Line(string or, DateOnly takenOn, decimal amount, DateOnly? feeDay = null) =>
        new(or,
            PhilippineTime.DayUtcRange(takenOn).StartUtc.AddHours(11),
            "Kim Chui", "1", FacilityCode.NPM, "Daily Fee", amount, feeDay ?? takenOn, null);

    private static async Task<ReportOfCollectionsDto> Run(
        IReadOnlyList<CollectorCollectionLine> lines,
        decimal officeRecorded = 0m,
        int officeReceipts = 0)
    {
        var handler = Handler(lines, officeRecorded, officeReceipts);
        var result = await handler.Handle(new GetReportOfCollectionsQuery(Collector, Aug1, Aug31), CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    private static GetReportOfCollectionsQueryHandler Handler(
        IReadOnlyList<CollectorCollectionLine> lines,
        decimal officeRecorded = 0m,
        int officeReceipts = 0)
    {
        var collectors = new Mock<ICollectorRepository>();
        collectors.Setup(c => c.GetByIdAsync(Collector, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(CollectorUser.Create(
                      "Juan Dels", "EEMO-2026-001", "juan.dels", null, null, TestPasswords.Hash("Secret123!")));

        var report = new Mock<ICollectorReportQueries>();
        report.Setup(r => r.GetCollectionsAsync(Collector, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new CollectorCollectionsData(
                  lines, Array.Empty<CollectorAbsenceLine>(), officeRecorded, officeReceipts, 0m, 0m));

        return new GetReportOfCollectionsQueryHandler(collectors.Object, report.Object);
    }
}
