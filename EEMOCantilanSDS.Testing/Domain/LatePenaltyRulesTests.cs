using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Testing;

public class LatePenaltyRulesTests
{
    private static readonly BillingPolicy Percent = BillingPolicy.StallTrackDefaults with { PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 2m };

    [Theory]
    [InlineData(1000, 20)]
    [InlineData(333.33, 6.67)]   // rounds to the centavo, away from zero
    public void APercentCharge_IsWorkedOnTheUnpaidRent(decimal unpaid, decimal expected) => Assert.Equal(expected, Percent.PenaltyFor(unpaid));

    [Fact]
    public void AFixedCharge_IgnoresTheAmountOwed()
    {
        var rules = BillingPolicy.StallTrackDefaults with { PenaltyBasis = LatePenaltyBasis.FixedAmount, PenaltyValue = 50m };

        Assert.Equal(50m, rules.PenaltyFor(1m));
        Assert.Equal(50m, rules.PenaltyFor(9_000m));
    }

    [Fact]
    public void AMonth_IsLateOnlyAfterItsDueDateAndGraceDays()
    {
        var rules = Percent with { DueDay = 5, GraceDays = 3 };

        Assert.Equal(0, rules.ChargesReached(2026, 11, new DateOnly(2026, 11, 8)));
        Assert.Equal(1, rules.ChargesReached(2026, 11, new DateOnly(2026, 11, 9)));
    }

    [Fact]
    public void AOneTimePenalty_NeverChargesTwice() => Assert.Equal(1, Percent.ChargesReached(2026, 1, new DateOnly(2026, 11, 4)));

    [Fact]
    public void ARepeatingPenalty_AddsOneChargeAMonth()
    {
        var rules = Percent with { DueDay = 5, PenaltyRepeatsMonthly = true };

        Assert.Equal(1, rules.ChargesReached(2026, 1, new DateOnly(2026, 2, 5)));
        Assert.Equal(2, rules.ChargesReached(2026, 1, new DateOnly(2026, 2, 6)));
        Assert.Equal(3, rules.ChargesReached(2026, 1, new DateOnly(2026, 3, 6)));
    }

    private static BillingPolicyVersion Version(DateOnly from, BillingPolicy rules) =>
        BillingPolicyVersion.Create(Guid.NewGuid(), from, from, rules, "Ordinance 1", Guid.NewGuid(), DateTime.UtcNow);

    [Fact]
    public void APolicy_NeverReachesBackOverADueDateAlreadyPast()
    {
        // In force from the 10th with a due day of the 5th: this month's due date came before it, so this month owes nothing under it. Next month does.
        var versions = new[] { Version(new DateOnly(2026, 11, 10), Percent with { DueDay = 5 }) };

        Assert.Null(BillingPolicyVersion.GoverningMonth(versions, 2026, 10));
        Assert.Null(BillingPolicyVersion.GoverningMonth(versions, 2026, 11));
        Assert.Same(versions[0], BillingPolicyVersion.GoverningMonth(versions, 2026, 12));
    }

    [Fact]
    public void AMonth_IsGovernedByTheVersionInForceOnItsDueDate()
    {
        var first = Version(new DateOnly(2026, 11, 1), Percent);
        var second = Version(new DateOnly(2026, 12, 15), Percent with { PenaltyValue = 5m });
        var versions = new[] { first, second };

        Assert.Same(first, BillingPolicyVersion.GoverningMonth(versions, 2026, 11));
        Assert.Same(second, BillingPolicyVersion.GoverningMonth(versions, 2026, 12));
    }
}
