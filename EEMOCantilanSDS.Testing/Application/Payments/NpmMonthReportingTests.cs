using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Queries.Reports.GetCollectionReport;

namespace EEMOCantilanSDS.Testing.Application.Payments;

public sealed class NpmMonthReportingTests
{
    [Fact]
    public async Task Collection_export_reproduces_the_closed_month_after_a_later_reversal()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25, businessDate: new(2025, 8, 31));
        f.Stall.SetSection(MarketSection.VegetableArea, null); await f.Db.SaveChangesAsync();
        var posted = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(posted.IsSuccess, posted.Error);
        var slaughter = new Mock<ISlaughterRepository>();
        slaughter.Setup(x => x.GetTransactionsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SlaughterTransactionDto>());
        var terminal = new Mock<ITrmRepository>();
        terminal.Setup(x => x.GetTripsByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TrmTripDto>());
        var tabo = new Mock<ITpmRepository>();
        tabo.Setup(x => x.GetMonthAttendanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TpmVendorAttendanceDto>());
        var facilities = new Mock<IFacilityRepository>();
        facilities.Setup(x => x.GetFacilityNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<FacilityCode, string> { [FacilityCode.NPM] = "Synthetic market" });
        GetCollectionReportQueryHandler Reader() => new(new FacilityReportsRepository(f.Db, f.Rates, f.Clock, f.Workflow),
            slaughter.Object, terminal.Object, tabo.Object, facilities.Object, f.Rates, f.Clock);
        var before = await Reader().Handle(new(2025, 8), default);
        Assert.True(before.IsSuccess, before.Error);
        var originalRow = Assert.Single(Assert.Single(before.Value!.Facilities).Rentals);
        Assert.Equal(900, originalRow.Coverage); Assert.Equal(900, originalRow.Collected); Assert.Equal(0, originalRow.CoverageBalance);
        f.Clock.Advance(TimeSpan.FromDays(1));
        Assert.True((await f.Workflow.ReverseAsync(posted.Value!.CollectionId, Guid.NewGuid(), "September verified reversal")).IsSuccess);
        var after = await Reader().Handle(new(2025, 8), default);
        Assert.True(after.IsSuccess, after.Error);
        Assert.Equal(originalRow, Assert.Single(Assert.Single(after.Value!.Facilities).Rentals));
        Assert.Equal(25, (await f.Workflow.ReadPositionAsync(f.Stall.Id, f.Month.PeriodStart))!.Outstanding);
        var original = Assert.Single(await f.Workflow.ReadCashEventsAsync(f.Stall.Id, f.Month.PeriodStart));
        Assert.Equal(posted.Value.CollectionId, original.CollectionId); Assert.Equal(25, original.Received);
        Assert.Equal(-25, original.CorrectionEffect); Assert.Equal(25, await f.Db.DailyCollections.CountAsync());
    }

    [Fact]
    public async Task Facility_weekly_query_returns_monthly_scope_guidance_without_inventing_payment_days()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(paidDays: 25, businessDate: new(2025, 8, 26));
        var reports = new FacilityReportsRepository(f.Db, f.Rates, f.Clock, f.Workflow);
        var facilities = new Moq.Mock<EEMOCantilanSDS.Application.Common.Interface.Persistence.IFacilityRepository>();
        facilities.Setup(x => x.GetByCodeAsync(FacilityCode.NPM, Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(await f.Db.Facilities.SingleAsync());
        var handler = new EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityReports.GetFacilityReportsQueryHandler(
            reports, facilities.Object, CacheTestDoubles.PassthroughCache, CacheTestDoubles.Tenant,
            new EEMOCantilanSDS.Application.Common.Caching.EemoCacheOptions());
        var result = await handler.Handle(new(FacilityCode.NPM, ReportPeriod.Weekly, 2025, 8, 4), default);
        Assert.False(result.IsSuccess); Assert.Equal(EEMOCantilanSDS.Application.Common.ResultStatus.Invalid, result.Status);
        Assert.Contains("monthly or yearly", result.Error);
        Assert.Equal(25, await f.Db.DailyCollections.CountAsync()); Assert.Empty(await f.Db.Collections.ToListAsync());
    }

    [Fact]
    public async Task Later_reversal_reopens_month_without_rewriting_the_prior_cutoff_or_cash_date()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(paidDays: 25, businessDate: new(2025, 8, 31));
        var posted = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(posted.IsSuccess, posted.Error);
        var firstDate = f.Clock.PhilippineToday;
        f.Clock.Advance(TimeSpan.FromDays(1));
        Assert.True((await f.Workflow.ReverseAsync(posted.Value!.CollectionId, Guid.NewGuid(), "Later verified reversal")).IsSuccess);
        Assert.Equal(0, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, firstDate))!.Outstanding);
        Assert.Equal(25, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, f.Clock.PhilippineToday))!.Outstanding);
        Assert.Equal(0, (await f.Workflow.ReadReportPositionsAsync([f.Stall.Id], f.Month.PeriodStart, firstDate))[f.Stall.Id]!.Position.Outstanding);
        Assert.Equal(25, (await f.Workflow.ReadReportPositionsAsync([f.Stall.Id], f.Month.PeriodStart, f.Clock.PhilippineToday))[f.Stall.Id]!.Position.Outstanding);
        Assert.Equal(25, Assert.Single(await f.Workflow.ReadCashMovementsAsync(f.Month.FacilityId, firstDate, firstDate)).Amount);
        Assert.Equal(-25, Assert.Single(await f.Workflow.ReadCashMovementsAsync(f.Month.FacilityId, f.Clock.PhilippineToday, f.Clock.PhilippineToday)).Amount);
        var tenant = Moq.Mock.Of<EEMOCantilanSDS.Application.Common.Tenancy.ICurrentMunicipalityAccessor>(x => x.MunicipalityId == f.TenantId);
        var reader = new EEMOCantilanSDS.Application.Queries.Revenue.GetCanonicalMonthlyIncome.GetCanonicalMonthlyIncomeQueryHandler(f.Db, f.User, tenant, f.Clock);
        var asOf = await reader.Handle(new(2025, 8, EEMOCantilanSDS.Application.Dtos.Revenue.CanonicalReportingBasis.AsOf,
            new DateTimeOffset(2025, 8, 31, 15, 59, 59, TimeSpan.Zero)), default);
        var latest = await reader.Handle(new(2025, 8, EEMOCantilanSDS.Application.Dtos.Revenue.CanonicalReportingBasis.LatestCorrected), default);
        Assert.True(asOf.IsSuccess, asOf.Error); Assert.True(latest.IsSuccess, latest.Error);
        Assert.Equal(25, asOf.Value!.GrandTotal.NetCollected);
        Assert.Equal(0, latest.Value!.GrandTotal.NetCollected);
        Assert.Equal(-25, latest.Value.GrandTotal.CorrectionEffectDatedOutsidePeriod);
        Assert.Single(await f.Db.Collections.ToListAsync()); Assert.Equal(25, await f.Db.DailyCollections.CountAsync());
    }

    [Fact]
    public async Task Activated_month_does_not_leave_a_phantom_daily_open_item_on_another_stalls_cash_date()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(paidDays: 25, businessDate: new(2025, 8, 26));
        f.Stall.SetSection(MarketSection.VegetableArea, null);
        var other = EEMOCantilanSDS.Domain.Entities.Facilities.Stall.Create(f.Month.FacilityId, "SECOND", 900,
            ApplicableFees.DailyRental, municipalityId: f.TenantId, section: MarketSection.VegetableArea);
        var contract = EEMOCantilanSDS.Domain.Entities.Facilities.Contract.Create(other.Id, "Other synthetic occupant", "Other", new(2025, 1, 1), 3, 900);
        NpmRentMonthTests.StampTenant(contract, f.TenantId);
        var day = DailyCollection.Create(other.Id, f.BusinessDate, dailyFee: 35);
        NpmRentMonthTests.StampTenant(day, f.TenantId); day.MarkPaid("Synthetic evidence", null);
        f.Db.AddRange(other, contract, day); await f.Db.SaveChangesAsync();
        Assert.True((await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!))).IsSuccess);
        var reader = new CollectorRepository(f.Db, f.Rates, new FixedClock(new(2025, 8, 26, 4, 0, 0, DateTimeKind.Utc)));
        var report = await reader.GetCollectorReportAsync(Guid.NewGuid(), [FacilityCode.NPM], f.Month.PeriodStart, f.BusinessDate, default);
        Assert.Equal(0, report.Periods.Single(x => x.PeriodDate == f.BusinessDate).OpenItemCount);
        Assert.Equal(26, await f.Db.DailyCollections.CountAsync()); // 25 real installments plus the other stall's real payment.
    }

    [Fact]
    public async Task Later_approved_credit_is_not_projected_into_an_earlier_report()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(businessDate: new(2025, 8, 26));
        var revised = await f.Workflow.ApplyReviewedCreditAsync(new(Guid.NewGuid(), f.Stall.Id, f.Month.PeriodStart,
            f.Month.SettlementVersion, 25, "Office decision", "Explicit credit"));
        Assert.True(revised.IsSuccess, revised.Error);
        Assert.Equal(0, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, new(2025, 8, 25)))!.Credits);
        Assert.Equal(25, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, new(2025, 8, 26)))!.Credits);
    }

    [Fact]
    public async Task Monthly_report_compliance_trend_sections_and_rent_breakdown_include_exact_cash_once_without_a_fake_day()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25, businessDate: new(2025, 8, 26));
        f.Stall.SetSection(MarketSection.VegetableArea, null); await f.Db.SaveChangesAsync();
        var posted = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(posted.IsSuccess, posted.Error);
        var reports = new FacilityReportsRepository(f.Db, f.Rates, new FixedClock(new(2025, 8, 26, 4, 0, 0, DateTimeKind.Utc)), f.Workflow);
        var report = await reports.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2025, 8, null, default);
        Assert.Equal(900, report.TotalRevenue); Assert.Equal(0, report.PendingPaymentAmount);
        var row = Assert.Single(report.StallCompliance);
        Assert.Equal(900, row.AmountPaid); Assert.Equal(0, row.Balance);
        Assert.Equal(900, row.FullPeriodRentObligation);
        Assert.Equal(900, report.RevenueTrend.Single(x => x.IsCurrentPeriod).Revenue);
        Assert.Equal(900, report.SectionBreakdown.Sum(x => x.Revenue));
        Assert.Equal(900, report.FeeTypeBreakdown!.DailyFeeAmount);
        Assert.Equal(25, report.FeeTypeBreakdown.PaidDayRecords);
        Assert.Equal(900, report.TopStalls.First().Revenue);
        Assert.Equal(25, f.Db.DailyCollections.Count());
    }

    [Fact]
    public async Task Backdated_cash_is_not_projected_into_an_as_known_cutoff_before_it_was_recorded()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25, businessDate: new(2025, 8, 26));
        var date = new DateOnly(2025, 8, 25);
        var quote = (await f.Workflow.QuoteAsync(f.Stall.Id, f.Month.PeriodStart, date, NpmRentPaymentScope.WholeMonth)).Value!;
        var posted = await f.Workflow.PostAsync(new(Guid.NewGuid(), f.Stall.Id, f.Month.PeriodStart, date, quote.Scope, quote.EligibleAmount, quote.Token));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(25, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, date))!.Outstanding);
        Assert.Empty(await f.Workflow.ReadCashMovementsAsync(f.Month.FacilityId, date, date));
        Assert.Equal(25, Assert.Single(await f.Workflow.ReadCashMovementsAsync(f.Month.FacilityId, date, date, asOf: f.BusinessDate)).Amount);
        Assert.Equal(0, (await f.Workflow.ReadAsOfPositionAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate))!.Outstanding);
    }

    [Fact]
    public async Task Reversal_remains_a_separate_signed_movement_on_its_effective_date()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 35, paidDays: 25);
        var posted = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.True((await f.Workflow.ReverseAsync(posted.Value!.CollectionId, Guid.NewGuid(), "Fixture correction")).IsSuccess);
        var cash = await f.Workflow.ReadCashMovementsAsync(f.Month.FacilityId, f.BusinessDate, f.BusinessDate);
        Assert.Equal(2, cash.Count); Assert.Equal(0, cash.Sum(x => x.Amount));
        Assert.Contains(cash, x => x.Amount == 25); Assert.Contains(cash, x => x.Amount == -25);
        Assert.Single(f.Db.Collections); Assert.Equal(25, (await f.Quote()).Value!.Outstanding);
    }

    [Fact]
    public async Task Unrepresented_legacy_monthly_cash_requires_reconciliation_instead_of_being_lost_from_the_opening()
    {
        await using var f = await NpmMonthWorkflowFixture.Create();
        var record = PaymentRecord.Create(f.Stall.Id, 2025, 8, 900);
        NpmRentMonthTests.StampTenant(record, f.TenantId);
        record.RecordPayment("Fixture evidence", Guid.NewGuid(), PaymentStatus.Partial, 100);
        f.Db.PaymentRecords.Add(record); await f.Db.SaveChangesAsync();
        var quote = await f.Quote();
        Assert.False(quote.IsSuccess); Assert.Contains("Legacy monthly payment evidence", quote.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow.ReadReportPositionsAsync([f.Stall.Id], f.Month.PeriodStart, f.BusinessDate));
        Assert.Empty(f.Db.Collections);
    }
}
