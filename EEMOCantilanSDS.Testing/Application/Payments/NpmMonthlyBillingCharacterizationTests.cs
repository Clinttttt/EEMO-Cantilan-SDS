using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Common.Billing;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Testing.Support;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Payments;

/// <summary>
/// Task 1 records the existing calculation, including explicitly named limitations. It does not approve
/// these limitations as future policy. Pending acceptance cases live in NPM_MONTHLY_BILLING_STANDARD_REVIEW_20261016.md.
/// No API, real database or production writer is used by this fixture.
/// </summary>
public sealed class NpmMonthlyBillingCharacterizationTests
{
    internal static (Stall Stall, List<DailyCollection> Rows, NpmMonthSettlementService Service) Fixture(
        NpmMonthBasis basis = NpmMonthBasis.RentGoal, decimal dailyFee = 30m, decimal monthlyRent = 900m)
    {
        var facility = Facility.Create(FacilityCode.NPM, "Test market", "TEST");
        var stall = Stall.Create(facility.Id, "A-1", 7777m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        stall.Contracts.Add(Contract.Create(stall.Id, "Test occupant", "Test contract", new DateOnly(2025, 1, 1), 3, 7777m));
        var entries = new List<FeeRateEntry> { new(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, monthlyRent, new(2020, 1, 1)) };
        if (dailyFee > 0m) entries.Add(new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, dailyFee, new(2020, 1, 1)));
        var rates = new Mock<IFeeRateResolver>();
        rates.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FeeRateSnapshot(entries, null, basis));
        var rows = new List<DailyCollection>();
        var days = new Mock<IDailyCollectionRepository>();
        days.Setup(x => x.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => rows.ToArray());
        days.Setup(x => x.GetByStallAndDateAsync(stall.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, DateOnly date, CancellationToken ct) => rows.SingleOrDefault(x => x.CollectionDate == date));
        days.Setup(x => x.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((row, ct) => rows.Add(row)).Returns(Task.CompletedTask);
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(x => x.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NpmMarketClosure>());
        return (stall, rows, new(days.Object, closures.Object, rates.Object, new FixedClock(new DateTime(2026, 10, 16, 4, 0, 0, DateTimeKind.Utc))));
    }

    [Theory]
    [InlineData(NpmMonthBasis.RentGoal, 2, 900, 60, 28)]
    [InlineData(NpmMonthBasis.RentGoal, 8, 900, 0, 30)]
    [InlineData(NpmMonthBasis.PureDays, 2, 840, 0, 28)]
    [InlineData(NpmMonthBasis.PureDays, 8, 930, 0, 31)]
    public async Task Existing_basis_controls_closed_month_quote_without_repricing_contract_evidence(
        NpmMonthBasis basis, int month, decimal expected, decimal adjustment, int days)
    {
        var (stall, _, service) = Fixture(basis);
        var quote = await service.ComputeWholeMonthPayableAsync(stall, 2025, month, CancellationToken.None);
        Assert.Equal(expected, quote.Obligation);
        Assert.Equal(expected, quote.Amount);
        Assert.Equal(adjustment, quote.Adjustment);
        Assert.Equal(days, quote.Days);
        Assert.Equal(7777m, stall.MonthlyRate); // Descriptive/stored stall rent does not become NPM's approved amount.
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task Three_recorded_installments_reduce_monthly_balance_and_settlement_cannot_repeat(int month)
    {
        var (stall, rows, service) = Fixture();
        for (var day = 1; day <= 3; day++)
        {
            var row = DailyCollection.Create(stall.Id, new DateOnly(2025, month, day), "Test", 30m);
            row.MarkPaid("", null, updatedBy: "Test"); rows.Add(row);
        }
        var quote = await service.ComputeWholeMonthPayableAsync(stall, 2025, month, CancellationToken.None);
        Assert.Equal(900m, quote.Obligation);
        Assert.Equal(90m, quote.Collected);
        Assert.Equal(810m, quote.Amount);
        await service.SettleWholeMonthAsync(stall, 2025, month, null, "Test", CancellationToken.None, quote.Amount);
        Assert.Equal(900m, rows.Sum(x => x.DailyFee));
        var count = rows.Count;
        Assert.Empty(await service.SettleWholeMonthAsync(stall, 2025, month, null, "Test", CancellationToken.None));
        Assert.Equal(count, rows.Count);
        Assert.Equal(0m, (await service.ComputeWholeMonthPayableAsync(stall, 2025, month, CancellationToken.None)).Amount);
    }

    [Theory]
    [InlineData(900, 875)]
    [InlineData(1000, 980)]
    public async Task Current_limitation_whole_quote_stops_at_whole_daily_installments(decimal monthlyRent, decimal quoted)
    {
        var (stall, _, service) = Fixture(dailyFee: 35m, monthlyRent: monthlyRent);
        var quote = await service.ComputeWholeMonthPayableAsync(stall, 2025, 8, CancellationToken.None);
        Assert.Equal(monthlyRent, quote.Obligation);
        Assert.Equal(quoted, quote.Amount);
        Assert.Equal(0m, quote.Adjustment);
        Assert.True(quote.Amount < quote.Obligation); // Pending future acceptance: exact monthly balance, no fabricated rate.
    }

    [Fact]
    public void Current_limitation_monthly_amount_without_daily_fee_does_not_price_partial_occupancy()
    {
        var rule = DailyBilledMonthRules.For(NpmMonthBasis.RentGoal);
        Assert.Equal(1900m, rule.Obligation(0m, 1900m, 31, 31));
        Assert.Equal(0m, rule.Obligation(0m, 1900m, 31, 15));
        // This low-level full-month result is not proof of supported onboarding, registration, credits or posting.
    }

    [Fact]
    public void Current_coverage_reference_differs_from_net_obligation_for_fully_excused_February()
    {
        var rule = DailyBilledMonthRules.For(NpmMonthBasis.RentGoal);
        var obligation = rule.Obligation(30m, 900m, 28, 28);
        var credit = DomainRules.DailyBilledMonthCredit(30m, obligation, 28, 28);
        Assert.Equal(0m, DomainRules.DailyBilledMonthOutstanding(obligation, 0m, credit));
        Assert.Equal(60m, rule.Coverage(30m, 900m, 28, 28));
        // This is a reference-column discrepancy, not authorization to charge an absent holder another 60 pesos.
    }
}
