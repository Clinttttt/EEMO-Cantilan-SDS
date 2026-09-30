using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// An approved penalty is an append-only, effective-dated version: it cannot describe an impossible amount rule, and the
/// version in force on a date is deterministic. (IA-049)
/// </summary>
public sealed class PenaltyDefinitionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static PenaltyDefinition Version(
        string code = "LATE_PAYMENT", DateOnly? effective = null, GovernedServiceBasis basis = GovernedServiceBasis.FixedAmount,
        decimal? fixedAmount = 50m, decimal? ceiling = null, bool active = true, DateTime? createdAt = null) =>
        PenaltyDefinition.Create(Tenant, code, effective ?? new DateOnly(2026, 1, 1), "Late payment", null, basis,
            basis == GovernedServiceBasis.FixedAmount ? fixedAmount : null, ceiling, active, "head", createdAt);

    [Theory]
    [InlineData("late")]          // must be upper-case
    [InlineData("1LATE")]         // must start with a letter
    [InlineData("L")]             // too short
    [InlineData("LATE PAYMENT")]  // no spaces
    public void OnlyAStableUpperCaseCodeIsAccepted(string code) =>
        Assert.Throws<ArgumentException>(() => Version(code));

    [Fact]
    public void AFixedPenaltyNeedsAPositiveCentAmount_AndNoCeiling()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Version(fixedAmount: 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => Version(fixedAmount: 10.005m));
        Assert.Throws<ArgumentException>(() => PenaltyDefinition.Create(Tenant, "FIXED_WITH_CAP", new DateOnly(2026, 1, 1),
            "x", null, GovernedServiceBasis.FixedAmount, 50m, 100m, true, "head"));
    }

    [Fact]
    public void AManuallyApprovedPenaltyHasNoFixedAmount_AndAnOptionalPositiveCeiling()
    {
        Assert.Throws<ArgumentException>(() => PenaltyDefinition.Create(Tenant, "MANUAL", new DateOnly(2026, 1, 1),
            "x", null, GovernedServiceBasis.DirectApprovedAmount, 50m, null, true, "head"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Version(basis: GovernedServiceBasis.DirectApprovedAmount, ceiling: 0m));
        Assert.Null(Version(basis: GovernedServiceBasis.DirectApprovedAmount, ceiling: null).MaximumAmount);
    }

    [Theory]
    [InlineData(50.0, null)]
    [InlineData(49.99, "AMOUNT_NOT_APPROVED")]
    [InlineData(50.01, "AMOUNT_NOT_APPROVED")]
    [InlineData(0.0, "AMOUNT_INVALID")]
    [InlineData(-5.0, "AMOUNT_INVALID")]
    public void AFixedPenaltyAcceptsExactlyItsApprovedAmount(double amount, string? expected) =>
        Assert.Equal(expected, Version().CheckAmount((decimal)amount));

    [Fact]
    public void AManualPenaltyIsHeldToItsCeiling()
    {
        var capped = Version(basis: GovernedServiceBasis.DirectApprovedAmount, ceiling: 200m);
        var open = Version(basis: GovernedServiceBasis.DirectApprovedAmount, ceiling: null);

        Assert.Null(capped.CheckAmount(200m));
        Assert.Equal("AMOUNT_ABOVE_CEILING", capped.CheckAmount(200.01m));
        Assert.Equal("AMOUNT_INVALID", capped.CheckAmount(10.123m));
        Assert.Null(open.CheckAmount(99_999m));
    }

    [Fact]
    public void TheVersionInForceIsTheLatestEffectiveOnOrBeforeTheDate_AndTheLastRecordedOnATie()
    {
        var first = Version(effective: new DateOnly(2026, 1, 1), fixedAmount: 50m, createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var second = Version(effective: new DateOnly(2026, 6, 1), fixedAmount: 60m, createdAt: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var correction = Version(effective: new DateOnly(2026, 6, 1), fixedAmount: 65m, createdAt: new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc));
        var other = Version(code: "OTHER_FINE", effective: new DateOnly(2026, 2, 1), fixedAmount: 10m);
        var all = new[] { first, second, correction, other };

        Assert.Same(first, PenaltyDefinition.Resolve(all, "LATE_PAYMENT", new DateOnly(2026, 5, 31)));
        Assert.Same(correction, PenaltyDefinition.Resolve(all, "LATE_PAYMENT", new DateOnly(2026, 6, 1)));
        Assert.Null(PenaltyDefinition.Resolve(all, "LATE_PAYMENT", new DateOnly(2025, 12, 31)));
        Assert.Null(PenaltyDefinition.Resolve(all, "UNKNOWN", new DateOnly(2026, 12, 31)));
    }
}
