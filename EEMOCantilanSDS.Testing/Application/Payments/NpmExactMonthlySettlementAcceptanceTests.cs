using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Payments;

/// <summary>
/// Task 2A exact-balance acceptance specifications. Pending cases intentionally require a decision about
/// valid allocation evidence; see NPM_EXACT_MONTHLY_SETTLEMENT_DESIGN_20261016.md. This fixture is in-process only.
/// </summary>
public sealed class NpmExactMonthlySettlementAcceptanceTests
{
    [Theory]
    [InlineData(900, 2, 0)]
    [InlineData(900, 2, 2)]
    [InlineData(900, 8, 0)]
    [InlineData(900, 8, 2)]
    [InlineData(1000, 8, 0)]
    [InlineData(1000, 8, 2)]
    [InlineData(900, 8, 25)]
    [InlineData(1000, 8, 28)]
    public async Task Full_month_quote_must_equal_exact_remaining_approved_obligation(decimal monthlyRent, int month, int paidDays)
    {
        await using var f = await NpmMonthWorkflowFixture.Create(monthlyRent, 35, month, paidDays);
        var rows = f.Db.DailyCollections.ToList();
        var quote = await NpmMonthSettlementBridgeTests.Bridge(f).ComputeWholeMonthPayableAsync(f.Stall, 2025, month, CancellationToken.None);
        Assert.Equal(monthlyRent, quote.Obligation);
        Assert.Equal(monthlyRent - 35m * paidDays, quote.Amount);
        Assert.Equal(paidDays, rows.Count); // Quoting must not create records or rewrite earlier cash.
        Assert.All(rows, row => { Assert.Equal(35m, row.DailyFee); Assert.Equal("Historical fixture evidence", row.ORNumber); });
        var posted = await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(0, (await NpmMonthSettlementBridgeTests.Bridge(f).ComputeWholeMonthPayableAsync(f.Stall, 2025, month, default)).Amount);
        Assert.Equal(paidDays, f.Db.DailyCollections.Count());
    }

    [Theory]
    [InlineData(NpmMonthBasis.RentGoal, 30, 900, 2, 900)]
    [InlineData(NpmMonthBasis.RentGoal, 30, 900, 8, 900)]
    [InlineData(NpmMonthBasis.RentGoal, 35, 1000, 2, 1000)]
    [InlineData(NpmMonthBasis.PureDays, 35, 900, 2, 980)]
    [InlineData(NpmMonthBasis.PureDays, 35, 900, 8, 1085)]
    public async Task Existing_Cantilan_short_month_and_PureDays_quotes_remain_compatible(
        NpmMonthBasis basis, decimal dailyFee, decimal monthlyRent, int month, decimal expected)
    {
        var (stall, rows, service) = NpmMonthlyBillingCharacterizationTests.Fixture(basis, dailyFee, monthlyRent);
        var quote = await service.ComputeWholeMonthPayableAsync(stall, 2025, month, CancellationToken.None);
        Assert.Equal(expected, quote.Amount);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Existing_partial_day_cannot_be_assumed_to_mean_month_settled_without_residual_debt()
    {
        var (stall, rows, service) = NpmMonthlyBillingCharacterizationTests.Fixture(dailyFee: 35m);
        for (var day = 1; day <= 25; day++)
        {
            var row = DailyCollection.Create(stall.Id, new DateOnly(2025, 8, day), "Test", 35m);
            row.MarkPaid("Historical evidence", null); rows.Add(row);
        }
        var terminal = DailyCollection.Create(stall.Id, new DateOnly(2025, 8, 26), "Test", 35m);
        var originalCollection = Guid.NewGuid();
        terminal.ApplyCanonicalPartial(originalCollection, 25m, null, "Test"); rows.Add(terminal);
        var quote = await service.ComputeWholeMonthPayableAsync(stall, 2025, 8, CancellationToken.None);
        Assert.Equal(900m, quote.Collected);
        Assert.Equal(0m, DomainRules.DailyBilledMonthOutstanding(quote.Obligation!.Value, quote.Collected!.Value, quote.Credits!.Value));
        Assert.True(terminal.IsPartlyPaid);
        Assert.Equal(10m, terminal.RemainingBaseFee); // Arrears currently sums this outside the monthly cap.
        Assert.Equal(35m, terminal.BaseFee);
        Assert.Equal(originalCollection, terminal.CanonicalCollectionId);
        terminal.ReverseCanonicalPartial(25m, null, "Test reversal");
        Assert.Equal(35m, terminal.RemainingBaseFee);
        Assert.Equal(35m, terminal.BaseFee);
        Assert.All(rows.Take(25), row => Assert.Equal(35m, row.DailyFee));
    }

    [Theory]
    [InlineData(900, 25, 25)]
    [InlineData(1000, 28, 20)]
    public async Task Existing_part_payment_plan_cannot_clear_the_stranded_month_remainder(
        decimal monthlyRent, int paidDays, decimal remainder)
    {
        var (stall, rows, service) = NpmMonthlyBillingCharacterizationTests.Fixture(dailyFee: 35m, monthlyRent: monthlyRent);
        for (var day = 1; day <= paidDays; day++)
        {
            var row = DailyCollection.Create(stall.Id, new DateOnly(2025, 8, day), "Test", 35m);
            row.MarkPaid("Historical evidence", null); rows.Add(row);
        }
        var plan = await service.PlanPartPaymentAsync(stall, 2025, 8, remainder, CancellationToken.None);
        Assert.Equal("NothingPayable", plan.Outcome);
        Assert.Equal(0m, plan.Applied);
        Assert.Empty(plan.Lines);
        Assert.Equal(monthlyRent - remainder, rows.Sum(x => x.DailyFee));
        Assert.All(rows, row => Assert.Equal("Historical evidence", row.ORNumber));
    }
}
