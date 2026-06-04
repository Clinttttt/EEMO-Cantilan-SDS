using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

public class FacilityReportsNpmDedupTests : RepositoryTestBase
{
    // Regression: a stall with BOTH a monthly payment and daily collections in the same period
    // must not have its daily collections counted again in the trend/top-stall widgets.
    [Fact]
    public async Task MonthlyTrend_DoesNotDoubleCount_DailyForMonthlyPaidStall()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);

        var payment = PaymentRecord.Create(stall.Id, 2026, 1, 900m);
        payment.UpdateStatus(PaymentStatus.Paid);

        var daily = DailyCollection.Create(stall.Id, new DateOnly(2026, 1, 15));
        daily.MarkPaid("OR-1", Guid.NewGuid());

        context.AddRange(facility, stall, payment, daily);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 1, null, CancellationToken.None);

        // Headline revenue already dedupes (daily excluded for the monthly-paid stall).
        Assert.Equal(900m, report.TotalRevenue);
        // The Jan-2026 trend point (last of 6) must equal the deduped headline, not headline + daily fee.
        Assert.Equal(report.TotalRevenue, report.RevenueTrend[^1].Revenue);
    }

    [Fact]
    public async Task WeeklyReport_NpmMonthlyEquivalentPayment_IsAllocatedAsThirtyPesosPerDay()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Daily Payor", "Daily Payor", new DateOnly(2026, 1, 1), 3, 900m);
        var payment = PaymentRecord.Create(stall.Id, 2026, 6, 900m);
        payment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Weekly, 2026, 6, 1, CancellationToken.None);

        Assert.Equal(210m, report.TotalRevenue);
        Assert.Equal(100m, report.CollectionRate);
        Assert.All(report.RevenueTrend, p => Assert.Equal(30m, p.Revenue));

        var compliance = Assert.Single(report.StallCompliance);
        Assert.Equal("Paid", compliance.Status);
        Assert.Equal(210m, compliance.AmountPaid);
        Assert.Equal(0m, compliance.Balance);

        var vegetable = report.SectionBreakdown.Single(s => s.SectionName == "Vegetable Area");
        Assert.Equal(210m, vegetable.Revenue);
        Assert.Equal(100m, vegetable.Percentage);
    }

    [Fact]
    public async Task WeeklyReport_UnpaidNpmStall_OwesOnlySelectedDailyObligation()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Unpaid Payor", "Unpaid Payor", new DateOnly(2026, 1, 1), 3, 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Weekly, 2026, 6, 1, CancellationToken.None);

        var compliance = Assert.Single(report.StallCompliance);
        Assert.Equal("Unpaid", compliance.Status);
        Assert.Equal(0m, compliance.AmountPaid);
        Assert.Equal(210m, compliance.Balance);
        Assert.Equal(210m, report.PendingPaymentAmount);
    }

    [Fact]
    public async Task YearlyReport_NpmComplianceUsesSelectedYearForEveryStall()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var ana = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var lorna = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var riki = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var pantom = Stall.Create(facility.Id, "4", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);

        var anaContract = Contract.Create(ana.Id, "Ana Villanueva", "Ana Villanueva", new DateOnly(2026, 1, 1), 3, 900m);
        var lornaContract = Contract.Create(lorna.Id, "Lorna Guevarra", "Lorna Guevarra", new DateOnly(2026, 1, 1), 3, 900m);
        var rikiContract = Contract.Create(riki.Id, "Riki Buenades", "Riki Buenades", new DateOnly(2026, 1, 1), 3, 900m);
        var pantomContract = Contract.Create(pantom.Id, "Pantom Goth", "Pantom Goth", new DateOnly(2026, 1, 1), 3, 900m);

        var anaPayment = PaymentRecord.Create(ana.Id, 2026, 6, 900m);
        anaPayment.UpdateStatus(PaymentStatus.Partial, 500m);
        var lornaPayment = PaymentRecord.Create(lorna.Id, 2026, 6, 900m);
        lornaPayment.UpdateStatus(PaymentStatus.Partial, 600m);
        var pantomPayment = PaymentRecord.Create(pantom.Id, 2026, 6, 900m);
        pantomPayment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, ana, lorna, riki, pantom,
            anaContract, lornaContract, rikiContract, pantomContract,
            anaPayment, lornaPayment, pantomPayment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Yearly, 2026, null, null, CancellationToken.None);

        Assert.Equal(41800m, report.PendingPaymentAmount);
        Assert.Equal(10450m, report.StallCompliance.Single(s => s.StallNo == "1").Balance);
        Assert.Equal(10350m, report.StallCompliance.Single(s => s.StallNo == "2").Balance);
        Assert.Equal(10950m, report.StallCompliance.Single(s => s.StallNo == "3").Balance);
        Assert.Equal(10050m, report.StallCompliance.Single(s => s.StallNo == "4").Balance);
    }

    [Fact]
    public async Task StallCompliance_ReportsPaidAndUnpaidRowsWithBalances()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var paidStall = Stall.Create(facility.Id, "101", 1000m, ApplicableFees.BaseRental);
        var unpaidStall = Stall.Create(facility.Id, "102", 1000m, ApplicableFees.BaseRental);

        var paidContract = Contract.Create(paidStall.Id, "Paid Occupant", "Paid Occupant", new DateOnly(2026, 1, 1), 3, 1000m);
        var unpaidContract = Contract.Create(unpaidStall.Id, "Unpaid Occupant", "Unpaid Occupant", new DateOnly(2026, 1, 1), 3, 1000m);

        var payment = PaymentRecord.Create(paidStall.Id, 2026, 6, 1000m);
        payment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, paidStall, unpaidStall, paidContract, unpaidContract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.TCC, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.Equal(2, report.StallCompliance.Count);

        var paid = report.StallCompliance.Single(r => r.StallNo == "101");
        Assert.Equal("Paid", paid.Status);
        Assert.Equal(0m, paid.Balance);
        Assert.Equal(1000m, paid.AmountPaid);

        var unpaid = report.StallCompliance.Single(r => r.StallNo == "102");
        Assert.Equal("Unpaid", unpaid.Status);
        Assert.Equal(1000m, unpaid.Balance);
    }

    [Fact]
    public async Task PendingSummary_MatchesStallComplianceBalances_WhenNpmPartialStallsHaveDailyCollections()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var partialA = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var partialB = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var unpaid = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var paid = Stall.Create(facility.Id, "4", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);

        var contractA = Contract.Create(partialA.Id, "Ana Villanueva", "Ana Villanueva", new DateOnly(2026, 1, 1), 3, 900m);
        var contractB = Contract.Create(partialB.Id, "Lorna Guevarra", "Lorna Guevarra", new DateOnly(2026, 1, 1), 3, 900m);
        var contractC = Contract.Create(unpaid.Id, "Riki Buenades", "Riki Buenades", new DateOnly(2026, 1, 1), 3, 900m);
        var contractD = Contract.Create(paid.Id, "Pantom Goth", "Pantom Goth", new DateOnly(2026, 1, 1), 3, 900m);

        var partialPaymentA = PaymentRecord.Create(partialA.Id, 2026, 6, 900m);
        partialPaymentA.UpdateStatus(PaymentStatus.Partial, 500m);
        var partialPaymentB = PaymentRecord.Create(partialB.Id, 2026, 6, 900m);
        partialPaymentB.UpdateStatus(PaymentStatus.Partial, 600m);
        var paidPayment = PaymentRecord.Create(paid.Id, 2026, 6, 900m);
        paidPayment.UpdateStatus(PaymentStatus.Paid);

        var partialADaily = Enumerable.Range(1, 14)
            .Select(day =>
            {
                var daily = DailyCollection.Create(partialA.Id, new DateOnly(2026, 6, day));
                daily.MarkPaid($"A-{day}", Guid.NewGuid());
                return daily;
            });
        var partialBDaily = Enumerable.Range(1, 10)
            .Select(day =>
            {
                var daily = DailyCollection.Create(partialB.Id, new DateOnly(2026, 6, day));
                daily.MarkPaid($"B-{day}", Guid.NewGuid());
                return daily;
            });

        context.AddRange(facility, partialA, partialB, unpaid, paid, contractA, contractB, contractC, contractD,
            partialPaymentA, partialPaymentB, paidPayment);
        context.AddRange(partialADaily);
        context.AddRange(partialBDaily);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.Equal(3, report.PendingPaymentCount);
        Assert.Equal(1600m, report.PendingPaymentAmount);
        Assert.Equal(report.PendingPaymentAmount, report.StallCompliance.Sum(s => s.Balance));
    }

    [Fact]
    public async Task DailyCollectionStreak_FullMonthlyPayment_CoversWholeMonth()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Occupant", "Occupant", new DateOnly(2026, 1, 1), 3, 900m);
        var payment = PaymentRecord.Create(stall.Id, 2026, 6, 900m);
        payment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        // ₱900 paid ÷ ₱30/day = 30 covered days = the whole month, none missed.
        Assert.NotNull(report.DailyCollectionStreak);
        Assert.Equal(30, report.DailyCollectionStreak!.CollectedDays);
        Assert.Equal(0, report.DailyCollectionStreak.MissedDays);
    }

    [Fact]
    public async Task DailyCollectionStreak_PaidPayorKeepsDayCollected_WhenAnotherPayorIsUnpaid()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var paidStall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var unpaidStall = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        var paidContract = Contract.Create(paidStall.Id, "Paid", "Paid", new DateOnly(2026, 1, 1), 3, 900m);
        var unpaidContract = Contract.Create(unpaidStall.Id, "Unpaid", "Unpaid", new DateOnly(2026, 1, 1), 3, 900m);
        var payment = PaymentRecord.Create(paidStall.Id, 2026, 6, 900m);
        payment.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, paidStall, unpaidStall, paidContract, unpaidContract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.NotNull(report.DailyCollectionStreak);
        Assert.Equal(30, report.DailyCollectionStreak!.CollectedDays);
        Assert.Equal(0, report.DailyCollectionStreak.MissedDays);
        Assert.Equal(0, report.DailyCollectionStreak.PartialDays);
        Assert.Equal(100, report.DailyCollectionStreak.CoverageRate);
        Assert.DoesNotContain(report.DailyCollectionStreak.Days, d => d.Status == "Partial");
    }

    [Fact]
    public async Task DailyCollectionStreak_PartialPayment_CoversProportionalDays()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Occupant", "Occupant", new DateOnly(2026, 1, 1), 3, 900m);
        var payment = PaymentRecord.Create(stall.Id, 2026, 6, 900m);
        payment.UpdateStatus(PaymentStatus.Partial, 180m); // ₱180 ÷ ₱30 = 6 covered days

        context.AddRange(facility, stall, contract, payment);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.NotNull(report.DailyCollectionStreak);
        Assert.Equal(6, report.DailyCollectionStreak!.CollectedDays);
    }

    [Fact]
    public async Task SectionBreakdown_EmptyNpmSections_UsesReadableSectionNames()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        context.Add(facility);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var report = await repo.GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 6, null, CancellationToken.None);

        Assert.Contains(report.SectionBreakdown, s => s.SectionName == "Vegetable Area");
        Assert.Contains(report.SectionBreakdown, s => s.SectionName == "Fish Section");
        Assert.Contains(report.SectionBreakdown, s => s.SectionName == "Meat Section");
        Assert.DoesNotContain(report.SectionBreakdown, s => s.SectionName.Contains("Section", StringComparison.Ordinal) && !s.SectionName.Contains(' '));
    }
}
