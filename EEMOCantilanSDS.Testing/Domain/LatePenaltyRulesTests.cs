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

    // Ruled 2026-11-24 (RA 7160 Sec. 168): a 25% surcharge once, then 2% a month on the rent and surcharge, for at most 36 months.
    private static readonly BillingPolicy Sec168 = BillingPolicy.StallTrackDefaults with
    {
        PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 25m, InterestPercent = 2m, InterestMaxMonths = 36, DueDay = 5, GraceDays = 3
    };

    [Fact]
    public void TheSurchargeIsOnTheRent_AndTheInterestOnTheRentAndSurchargeTogether()
    {
        Assert.Equal(250m, Sec168.PenaltyFor(1_000m));
        Assert.Equal(25m, Sec168.InterestFor(1_000m + 250m));
        Assert.True(Sec168 is { ChargesSurcharge: true, ChargesInterest: true, ChargesPenalty: true });
    }

    [Fact]
    public void Interest_StartsTheSameDayAsTheSurcharge_AddsOneMonthAtATime_AndStopsAtItsLimit()
    {
        Assert.Equal(0, Sec168.InterestMonthsReached(2026, 1, new DateOnly(2026, 1, 8)));    // due the 5th, three days' grace
        Assert.Equal(1, Sec168.InterestMonthsReached(2026, 1, new DateOnly(2026, 1, 9)));    // first late day: surcharge and first month together
        Assert.Equal(1, Sec168.InterestMonthsReached(2026, 1, new DateOnly(2026, 2, 8)));
        Assert.Equal(2, Sec168.InterestMonthsReached(2026, 1, new DateOnly(2026, 2, 9)));
        Assert.Equal(36, Sec168.InterestMonthsReached(2020, 1, new DateOnly(2026, 11, 24)));
        Assert.Equal(0, (Sec168 with { InterestPercent = null, InterestMaxMonths = null }).InterestMonthsReached(2026, 1, new DateOnly(2026, 11, 24)));
    }

    [Fact]
    public void InterestRules_ThatCannotBeKept_AreRefused()
    {
        Assert.Null(Sec168.Problem());
        Assert.NotNull((Sec168 with { PenaltyRepeatsMonthly = true }).Problem());              // with interest, the surcharge is once
        Assert.NotNull((Sec168 with { InterestPercent = null }).Problem());                    // a limit with no rate
        Assert.NotNull((Sec168 with { InterestPercent = 0m }).Problem());
        Assert.NotNull((Sec168 with { InterestMaxMonths = 0 }).Problem());
        Assert.Null((Sec168 with { PenaltyBasis = LatePenaltyBasis.None, PenaltyValue = null }).Problem());   // interest alone is allowed
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
