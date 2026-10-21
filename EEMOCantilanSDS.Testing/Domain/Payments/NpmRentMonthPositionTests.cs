using EEMOCantilanSDS.Domain.Common.Billing;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

public sealed class NpmRentMonthPositionTests
{
    [Theory]
    [InlineData(1, 1, 30)] [InlineData(20, 20, 30)] [InlineData(20, 22, 90)]
    [InlineData(20, 19, 0)] [InlineData(31, 31, 30)] [InlineData(20, 32, 900)]
    public void Prospective_gross_rent_is_distinct_from_daily_earned_liability(int startDay, int cutoffDay, decimal earned)
    {
        var month = Month();
        var position = NpmRentMonthPosition.From(month, [], []);
        Assert.Equal(900, position.Assessment); Assert.Equal(900, position.WholePayment);
        Assert.Equal(earned, position.EarnedInstallmentLiability(30, month.PeriodStart.AddDays(startDay - 1), month.PeriodStart.AddDays(cutoffDay - 1)));
        Assert.Equal(900, position.Outstanding);
    }

    [Fact]
    public void Prospective_earned_liability_honors_credits_and_rejects_invalid_installment_or_ownership()
    {
        var month = Month(credit: 60); var position = NpmRentMonthPosition.From(month, [], []);
        Assert.Equal(30, position.EarnedInstallmentLiability(30, month.PeriodStart.AddDays(19), month.PeriodStart.AddDays(21)));
        Assert.Equal(840, position.EarnedInstallmentLiability(30, month.PeriodStart.AddDays(19), month.PeriodStart.AddMonths(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => position.EarnedInstallmentLiability(0, month.PeriodStart, month.PeriodStart));
        Assert.Throws<ArgumentException>(() => position.EarnedInstallmentLiability(30, month.PeriodStart.AddDays(-1), month.PeriodStart));
    }

    [Theory]
    [InlineData(900, 35, 25, 25)]
    [InlineData(1000, 35, 28, 20)]
    [InlineData(900, 30, 30, 0)]
    [InlineData(900, 30, 28, 60)]
    public void Exact_month_remainders_are_independent_of_whole_day_divisibility(decimal assessment, decimal daily, int count, decimal remaining)
    {
        var month = Month(assessment);
        var cash = Enumerable.Range(0, count).Select(_ => Receipt(month, daily)).ToArray();
        var position = NpmRentMonthPosition.From(month, cash, []);
        Assert.Equal(remaining, position.Outstanding);
        Assert.Equal(remaining, position.WholePayment);
        Assert.Equal(Math.Min(daily, remaining), position.DailyInstallment(daily));
        Assert.False(month.CanAcceptCollections); // Arithmetic alone never activates a writer.
    }

    [Fact]
    public void Thirty_installments_close_the_month_without_an_additional_day_31_rent()
    {
        var month = Month();
        var cash = Enumerable.Range(0, 30).Select(_ => Receipt(month, 30)).ToArray();
        var position = NpmRentMonthPosition.From(month, cash, []);
        Assert.Equal(900, position.CanonicalCash);
        Assert.Equal(0, position.DailyInstallment(30));
        Assert.Throws<InvalidOperationException>(() => position.RequirePayment(30, 30, position.Version));
    }

    [Theory]
    [InlineData(900, 875, 25)]
    [InlineData(1000, 980, 20)]
    public void A_terminal_month_allocation_and_its_reversal_have_no_artificial_daily_residual(decimal assessment, decimal prior, decimal terminal)
    {
        var month = Month(assessment);
        var before = Receipt(month, prior);
        var last = Receipt(month, terminal);
        var paid = NpmRentMonthPosition.From(month, [before, last], []);
        Assert.Equal(0, paid.Outstanding);
        Assert.Null(last.DailySourceId); // A real month payment, never a partly paid fabricated day.
        var reversal = new NpmRentCorrectionEvidence(Guid.NewGuid(), last.EvidenceId, month.MunicipalityId, -terminal);
        var reopened = NpmRentMonthPosition.From(month, [before, last], [reversal]);
        Assert.Equal(terminal, reopened.Outstanding);
        Assert.Equal(prior, reopened.CanonicalCash);
        Assert.Equal(terminal, last.Amount); // Original factual collection evidence unchanged.
    }

    [Fact]
    public void Historical_excess_is_preserved_and_never_turned_into_advance_credit()
    {
        var month = Month();
        var position = NpmRentMonthPosition.From(month, [Receipt(month, 930)], []);
        Assert.Equal(930, position.CanonicalCash);
        Assert.Equal(0, position.Outstanding);
        Assert.Equal(30, position.HistoricalExcess);
        Assert.Equal(0, position.Credits);
    }

    [Theory]
    [InlineData(2, 980)]
    [InlineData(8, 1085)]
    public void Pure_days_uses_its_approved_assessment_and_never_invents_a_fixed_goal(int calendarMonth, decimal assessment)
    {
        // The existing month rule remains the assessment authority, not this position projector.
        Assert.Equal(assessment, DailyBilledMonthRules.For(NpmMonthBasis.PureDays)
            .Obligation(35, 900, DateTime.DaysInMonth(2025, calendarMonth), DateTime.DaysInMonth(2025, calendarMonth)));
        var month = Month(assessment, basis: NpmMonthBasis.PureDays);
        var position = NpmRentMonthPosition.From(month, [], []);
        Assert.Equal(assessment, position.WholePayment);
    }

    [Fact]
    public void Monthly_only_approved_assessment_does_not_need_any_daily_rate_or_day_records()
    {
        var month = Month(1900);
        var position = NpmRentMonthPosition.From(month, [], []);
        position.RequirePayment(1900, 1900, position.Version);
        var paid = NpmRentMonthPosition.From(month, [Receipt(month, 1900)], []);
        Assert.Equal(0, paid.Outstanding);
        Assert.Equal(1900, paid.CanonicalCash);
    }

    [Fact]
    public void Explicit_credits_reduce_the_obligation_without_becoming_received_cash()
    {
        var month = Month(900, 60);
        var position = NpmRentMonthPosition.From(month, [Receipt(month, 30)], []);
        Assert.Equal(810, position.Outstanding); Assert.Equal(30, position.CanonicalCash);
        Assert.Equal(60, position.Credits);
    }

    [Fact]
    public void Stale_quote_overpayment_and_fractional_cents_are_rejected()
    {
        var month = Month();
        var position = NpmRentMonthPosition.From(month, [Receipt(month, 875)], []);
        Assert.Throws<InvalidOperationException>(() => position.RequirePayment(25, 25, position.Version - 1));
        Assert.Throws<InvalidOperationException>(() => position.RequirePayment(26, 26, position.Version));
        Assert.Throws<InvalidOperationException>(() => position.RequirePayment(25, 20, position.Version));
        Assert.Throws<ArgumentOutOfRangeException>(() => position.RequirePayment(24.999m, 25, position.Version));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("contract")]
    [InlineData("stall")]
    [InlineData("month")]
    public void Cross_source_cash_cannot_settle_another_month(string wrong)
    {
        var month = Month(); var evidence = Receipt(month, 30);
        evidence = wrong switch
        {
            "tenant" => evidence with { MunicipalityId = Guid.NewGuid() },
            "contract" => evidence with { ContractId = Guid.NewGuid() },
            "stall" => evidence with { StallId = Guid.NewGuid() },
            _ => evidence with { PeriodStart = evidence.PeriodStart.AddMonths(1) }
        };
        Assert.Throws<InvalidOperationException>(() => NpmRentMonthPosition.From(month, [evidence], []));
    }

    [Fact]
    public void A_canonical_daily_projection_cannot_be_added_a_second_time_as_legacy_cash()
    {
        var month = Month(); var daily = Guid.NewGuid();
        var legacy = Receipt(month, 30) with { EvidenceId = daily, Kind = NpmRentCashEvidenceKind.LegacyDaily, DailySourceId = daily };
        var canonical = Receipt(month, 30) with { Kind = NpmRentCashEvidenceKind.CanonicalDailyAllocation, DailySourceId = daily };
        Assert.Throws<InvalidOperationException>(() => NpmRentMonthPosition.From(month, [legacy, canonical], []));
        var valid = NpmRentMonthPosition.From(month, [canonical], []);
        Assert.Equal(30, valid.CanonicalCash); Assert.Equal(0, valid.LegacyCash);
    }

    [Fact]
    public void Duplicate_or_unattributable_corrections_fail_closed()
    {
        var month = Month(); var receipt = Receipt(month, 30);
        var reversal = new NpmRentCorrectionEvidence(Guid.NewGuid(), receipt.EvidenceId, month.MunicipalityId, -30);
        Assert.Throws<InvalidOperationException>(() => NpmRentMonthPosition.From(month, [receipt, receipt], []));
        Assert.Throws<InvalidOperationException>(() => NpmRentMonthPosition.From(month, [receipt], [reversal, reversal]));
        Assert.Throws<InvalidOperationException>(() => NpmRentMonthPosition.From(month, [receipt], [reversal with { FinancialEffectAmount = -31 }]));
        Assert.Throws<InvalidOperationException>(() => NpmRentMonthPosition.From(month, [receipt], [reversal with { MunicipalityId = Guid.NewGuid() }]));
        Assert.Throws<InvalidOperationException>(() => NpmRentMonthPosition.From(month, [receipt], [reversal with { OriginalEvidenceId = Guid.NewGuid() }]));
    }

    [Fact]
    public void Unreconciled_foundation_does_not_authorize_a_financial_position()
    {
        Assert.Throws<InvalidOperationException>(() => NpmRentMonthPosition.From(NpmRentMonthTests.Month(), [], []));
    }

    internal static NpmRentMonth Month(decimal assessment = 900, decimal credit = 0, NpmMonthBasis basis = NpmMonthBasis.RentGoal)
    {
        var month = NpmRentMonthTests.Month(NpmRentMonthTests.Evidence(assessment, credit, basis));
        month.Reconcile(1, NpmRentMonthTests.Reviewer, "Reviewer", "SuperAdmin", NpmRentMonthTests.Utc,
            "Synthetic approved owner, assessment and opening evidence; no runtime activation");
        return month;
    }

    internal static NpmRentCashEvidence Receipt(NpmRentMonth month, decimal amount) => new(Guid.NewGuid(), month.MunicipalityId,
        month.StallId, month.ContractId, month.PeriodStart, NpmRentCashEvidenceKind.CanonicalMonthAllocation, amount);
}
