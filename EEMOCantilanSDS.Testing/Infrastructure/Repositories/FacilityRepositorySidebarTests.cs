using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

public class FacilityRepositorySidebarTests : RepositoryTestBase
{
    private static readonly DateOnly Today = new(2026, 9, 21);

    private static (FixedClock Clock, PaymentRepository Payments, FacilityRepository Facilities) Repositories(
        EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext context)
    {
        var clock = new FixedClock(Today.ToDateTime(TimeOnly.MinValue).AddHours(-8));
        var payments = new PaymentRepository(context, new FeeRateResolver(context), clock);
        return (clock, payments, new FacilityRepository(context, clock, payments));
    }

    [Fact]
    public async Task NpmSidebarCountsOnlyCurrentStallsPendingToday()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stalls = Enumerable.Range(1, 3)
            .Select(i => Stall.Create(facility.Id, i.ToString(), 900m, ApplicableFees.DailyRental, MarketSection.VegetableArea))
            .ToArray();
        var contracts = stalls.Select((stall, i) =>
            Contract.Create(stall.Id, $"Vendor {i}", $"Vendor {i}", new DateOnly(2026, 1, 1), 3, 900m)).ToArray();

        // Paid today, absent today, and no status today respectively.
        var paidToday = DailyCollection.Create(stalls[0].Id, Today);
        paidToday.MarkPaid("OR-TODAY", Guid.NewGuid());
        var absentToday = DailyCollection.Create(stalls[1].Id, Today);
        absentToday.MarkAbsent();
        context.AddRange(facility);
        context.AddRange(stalls);
        context.AddRange(contracts);
        context.AddRange(paidToday, absentToday);
        await context.SaveChangesAsync();

        var (_, _, facilities) = Repositories(context);
        var summary = (await facilities.GetSidebarSummariesAsync(Today.Year, Today.Month, CancellationToken.None))
            .Single(f => f.Code == FacilityCode.NPM);

        Assert.Equal(1, summary.UnpaidCount);
    }

    [Fact]
    public async Task NpmSidebarCountsAStallPaidEarlierThisMonthAsPendingToday()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Vendor", "Vendor", new DateOnly(2026, 1, 1), 3, 900m);
        var paidYesterday = DailyCollection.Create(stall.Id, Today.AddDays(-1));
        paidYesterday.MarkPaid("OR-YESTERDAY", Guid.NewGuid());
        context.AddRange(facility, stall, contract, paidYesterday);
        await context.SaveChangesAsync();

        var (_, _, facilities) = Repositories(context);
        var summary = (await facilities.GetSidebarSummariesAsync(Today.Year, Today.Month, CancellationToken.None))
            .Single(f => f.Code == FacilityCode.NPM);

        Assert.Equal(1, summary.UnpaidCount);
    }

    [Fact]
    public async Task NpmSidebarDoesNotCountPayorsOnAMarketClosureDay()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Vendor", "Vendor", new DateOnly(2026, 1, 1), 3, 900m);

        // A market closure excuses every NPM payor; no per-stall absent row is required.
        context.AddRange(facility, stall, contract, NpmMarketClosure.Create(Today));
        await context.SaveChangesAsync();

        var (_, _, facilities) = Repositories(context);
        var summary = (await facilities.GetSidebarSummariesAsync(Today.Year, Today.Month, CancellationToken.None))
            .Single(f => f.Code == FacilityCode.NPM);

        Assert.Equal(0, summary.UnpaidCount);
    }

    [Fact]
    public async Task NpmSidebarExcludesClosedAndExpiredStalls()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var current = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, MarketSection.VegetableArea);
        var closed = Stall.Create(facility.Id, "2", 900m, ApplicableFees.DailyRental, MarketSection.VegetableArea);
        var expired = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, MarketSection.VegetableArea);
        closed.Close(Today);

        context.AddRange(
            facility,
            current,
            closed,
            expired,
            Contract.Create(current.Id, "Current", "Current", new DateOnly(2026, 1, 1), 3, 900m),
            Contract.Create(closed.Id, "Closed", "Closed", new DateOnly(2026, 1, 1), 3, 900m),
            Contract.Create(expired.Id, "Expired", "Expired", new DateOnly(2024, 1, 1), 1, 900m));
        await context.SaveChangesAsync();

        var (_, _, facilities) = Repositories(context);
        var summary = (await facilities.GetSidebarSummariesAsync(Today.Year, Today.Month, CancellationToken.None))
            .Single(f => f.Code == FacilityCode.NPM);

        Assert.Equal(1, summary.UnpaidCount);
    }

    [Fact]
    public async Task MonthlyFacilitySidebarStillUsesCurrentMonthPaymentRecords()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var paid = Stall.Create(facility.Id, "1", 900m, ApplicableFees.BaseRental);
        var unpaid = Stall.Create(facility.Id, "2", 900m, ApplicableFees.BaseRental);
        context.AddRange(
            facility,
            paid,
            unpaid,
            Contract.Create(paid.Id, "Paid", "Paid", new DateOnly(2026, 1, 1), 3, 900m),
            Contract.Create(unpaid.Id, "Unpaid", "Unpaid", new DateOnly(2026, 1, 1), 3, 900m));
        var payment = PaymentRecord.Create(paid.Id, Today.Year, Today.Month, 900m);
        payment.RecordPayment("OR-TCC", Guid.NewGuid(), PaymentStatus.Paid);
        context.Add(payment);
        await context.SaveChangesAsync();

        var (_, _, facilities) = Repositories(context);
        var summary = (await facilities.GetSidebarSummariesAsync(Today.Year, Today.Month, CancellationToken.None))
            .Single(f => f.Code == FacilityCode.TCC);

        Assert.Equal(1, summary.UnpaidCount);
    }
}
