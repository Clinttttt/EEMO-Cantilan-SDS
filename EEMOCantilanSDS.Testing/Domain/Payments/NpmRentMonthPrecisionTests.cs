namespace EEMOCantilanSDS.Testing;

/// <summary>PostgreSQL numeric(18,2) rounds inputs; reject invalid money before any persistence.</summary>
public sealed class NpmRentMonthPrecisionTests
{
    private const decimal Maximum = 9_999_999_999_999_999.99m;

    public static TheoryData<decimal, decimal> ExactAmounts => new()
    {
        { 0m, 0m }, { 0.01m, 0.01m }, { 1900.25m, 25.25m },
        { Maximum, 0m }, { Maximum, Maximum }, { 900.000m, 0.000m }
    };

    public static TheoryData<decimal, decimal> InvalidAmounts => new()
    {
        { 900.001m, 0m }, { 900.005m, 0m }, { 900m, 0.001m }, { 900m, 0.005m },
        { Maximum + 0.01m, 0m }, { Maximum, Maximum + 0.01m },
        { decimal.MaxValue, 0m }, { decimal.MinValue, 0m }, { 900m, -0.001m }
    };

    [Theory]
    [MemberData(nameof(ExactAmounts))]
    public void Exact_cent_and_numeric_boundary_values_are_preserved(decimal assessed, decimal credited)
    {
        var month = NpmRentMonthTests.Month(NpmRentMonthTests.Evidence(assessed, credited));
        Assert.Equal(assessed, month.AssessedAmount);
        Assert.Equal(credited, month.CreditedAmount);
        Assert.False(month.CanAcceptCollections);
    }

    [Theory]
    [MemberData(nameof(InvalidAmounts))]
    public void Fractional_cents_and_overflow_are_rejected_without_rounding_or_revision(decimal assessed, decimal credited)
    {
        var evidence = NpmRentMonthTests.Evidence(assessed, credited);
        Assert.Throws<ArgumentOutOfRangeException>(() => NpmRentMonthTests.Month(evidence));
        var month = NpmRentMonthTests.Month();
        Assert.Throws<ArgumentOutOfRangeException>(() => month.ReviseAssessment(evidence, 1,
            NpmRentMonthTests.Reviewer, "Reviewer", "SuperAdmin", NpmRentMonthTests.Utc, "Precision test"));
        Assert.Equal(900m, month.AssessedAmount);
        Assert.Equal(0m, month.CreditedAmount);
        Assert.Equal(1, month.SettlementVersion);
        Assert.Null(month.UpdatedAtUtc);
        Assert.False(month.CanAcceptCollections);
    }
}
