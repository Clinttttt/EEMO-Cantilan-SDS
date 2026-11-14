using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Testing;

public class BillingPolicyTests
{
    private static readonly BillingPolicy Defaults = BillingPolicy.StallTrackDefaults;

    [Fact]
    public void TheDefaults_AreOn_ButChargeNoPenalty_BecauseARateIsNeverAssumed()
    {
        Assert.True(Defaults.OldestMonthFirst);
        Assert.True(Defaults.AnnualPaymentAllowed);
        Assert.Equal(3, Defaults.MaxAdvanceMonths);
        Assert.False(Defaults.ChargesPenalty);
        Assert.Null(Defaults.Problem());
    }

    [Theory]
    [InlineData(null, 2026, 2, 28)]   // no due day is the month's last day
    [InlineData(null, 2024, 2, 29)]
    [InlineData(15, 2026, 2, 15)]
    [InlineData(31, 2026, 2, 28)]     // a short month clamps
    [InlineData(31, 2026, 3, 31)]
    public void TheDueDate_FallsInsideTheMonth(int? dueDay, int year, int month, int expectedDay)
    {
        Assert.Equal(new DateOnly(year, month, expectedDay), (Defaults with { DueDay = dueDay }).DueDate(year, month));
    }

    [Fact]
    public void MonthsAhead_StartNextMonth_AndCrossAYear()
    {
        Assert.Equal([new(2027, 1, 1), new(2027, 2, 1), new(2027, 3, 1)], Defaults.MonthsPayableAhead(new DateOnly(2026, 12, 15)).ToArray());
        Assert.Empty((Defaults with { MaxAdvanceMonths = 0 }).MonthsPayableAhead(new DateOnly(2026, 12, 15)));
    }

    [Theory]
    [InlineData(2023, 1, false)]
    [InlineData(2026, 11, false)]
    [InlineData(2026, 12, true)]
    [InlineData(2027, 1, true)]
    public void TheRules_CountMonthsFromWhereTheyBegin(int year, int month, bool counted)
    {
        Assert.Equal(counted, Defaults.Counts(year, month));
    }

    [Theory]
    [InlineData(2026, 10, false)]   // elapsed and before the start: holds nothing up
    [InlineData(2026, 11, true)]    // the month in progress, though the start is still ahead
    [InlineData(2026, 12, true)]
    public void TheMonthInProgress_IsPaidInOrder_EvenBeforeTheStartMonth(int year, int month, bool inOrder)
    {
        Assert.Equal(inOrder, Defaults.PaidInOrder(year, month, new DateOnly(2026, 11, 5)));
    }

    public static TheoryData<BillingPolicy> Unkeepable => new()
    {
        Defaults with { CountsMonthsFrom = default },
        Defaults with { CountsMonthsFrom = new DateOnly(2026, 12, 15) },
        Defaults with { DueDay = 0 },
        Defaults with { DueDay = 32 },
        Defaults with { GraceDays = -1 },
        Defaults with { GraceDays = 91 },
        Defaults with { MaxAdvanceMonths = -1 },
        Defaults with { MaxAdvanceMonths = 13 },
        Defaults with { PenaltyValue = 2m },
        Defaults with { PenaltyMonthCap = 2m },
        Defaults with { PenaltyRepeatsMonthly = true },
        Defaults with { PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid },
        Defaults with { PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 0m },
        Defaults with { PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 100.01m },
        Defaults with { PenaltyBasis = LatePenaltyBasis.FixedAmount, PenaltyValue = 10.005m },
        Defaults with { PenaltyBasis = LatePenaltyBasis.FixedAmount, PenaltyValue = 10m, PenaltyMonthCap = 0m },
    };

    [Theory]
    [MemberData(nameof(Unkeepable))]
    public void RulesThatCannotBeKept_AreRefused(BillingPolicy rules)
    {
        Assert.NotNull(rules.Problem());
        Assert.Throws<ArgumentException>(() => BillingPolicyVersion.Create(Guid.NewGuid(), new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4), rules, "Ordinance 1", Guid.NewGuid(), DateTime.UtcNow));
    }

    [Fact]
    public void TheVersionInForce_IsTheLatestEffective_ThenTheLastRecorded()
    {
        var today = new DateOnly(2026, 11, 4);
        var tenant = Guid.NewGuid();
        BillingPolicyVersion Version(DateOnly from, int grace, DateTime at) =>
            BillingPolicyVersion.Create(tenant, from, today, Defaults with { GraceDays = grace }, null, Guid.NewGuid(), at);
        var noon = new DateTime(2026, 11, 4, 4, 0, 0, DateTimeKind.Utc);

        var versions = new[] { Version(today, 1, noon), Version(today, 2, noon.AddHours(1)), Version(today.AddDays(10), 3, noon.AddHours(2)) };

        Assert.Equal(2, BillingPolicyVersion.InForce(versions, today)!.GraceDays);
        Assert.Equal(3, BillingPolicyVersion.InForce(versions, today.AddDays(10))!.GraceDays);
        Assert.Null(BillingPolicyVersion.InForce(versions, today.AddDays(-1)));
    }
}
