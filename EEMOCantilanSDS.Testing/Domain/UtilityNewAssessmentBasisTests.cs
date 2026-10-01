using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// IA-055 domain rule: a utility part with no recorded metered assessment takes a direct approved amount only; a recorded
/// metered part is historical evidence that may be kept exactly as recorded or restated as a direct amount.
/// </summary>
public class UtilityNewAssessmentBasisTests
{
    private static UtilityBill Metered() => UtilityBill.Create(Guid.NewGuid(), 2026, 8, 1200m, 1248m, 8m, 0m, 0m, 0m);

    [Fact]
    public void NewAssessmentBases_AreDirectApprovedOnly()
    {
        Assert.Equal(new[] { UtilityCalculationBasis.DirectApproved }, UtilityBill.NewAssessmentBases);
    }

    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(100, 100, 0, false)]
    [InlineData(100, 101, 0, true)]
    [InlineData(0, 0, 5, true)]
    public void IsMeteredIntent_IsConsumptionOrAPerUnitRate(decimal prev, decimal cur, decimal rate, bool expected)
    {
        Assert.Equal(expected, UtilityBill.IsMeteredIntent(prev, cur, rate));
    }

    [Fact]
    public void ARecordedMeteredPart_AllowsMeteredOnlyForThatPart()
    {
        var bill = Metered();

        Assert.True(bill.HasMeteredAssessment(CollectionSourcePart.Electricity));
        Assert.False(bill.HasMeteredAssessment(CollectionSourcePart.Water));
        Assert.Contains(UtilityCalculationBasis.Metered, bill.AllowedCalculationBases(CollectionSourcePart.Electricity));
        Assert.Equal(UtilityBill.NewAssessmentBases, bill.AllowedCalculationBases(CollectionSourcePart.Water));
    }

    [Fact]
    public void ADirectApprovedPart_IsNeverAMeteredAssessment()
    {
        var (p, c, r) = UtilityBill.DirectApprovedReadings(650m);
        var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 9, p, c, r, 0m, 0m, 0m);
        bill.SetCalculationBasis(UtilityCalculationBasis.DirectApproved, UtilityCalculationBasis.Metered);

        Assert.False(bill.HasMeteredAssessment(CollectionSourcePart.Electricity));
        Assert.Equal(UtilityBill.NewAssessmentBases, bill.AllowedCalculationBases(CollectionSourcePart.Electricity));
    }

    [Fact]
    public void Refuse_NewMonthReadings_ButNotADirectAmountOrAnUnbilledPart()
    {
        Assert.NotNull(UtilityBill.RefuseAssessment(null, CollectionSourcePart.Water, false, 40m, 56m, 5m));
        Assert.NotNull(UtilityBill.RefuseAssessment(null, CollectionSourcePart.Electricity, false, 0m, 0m, 8m));
        Assert.Null(UtilityBill.RefuseAssessment(null, CollectionSourcePart.Electricity, true, 0m, 0m, 0m));
        Assert.Null(UtilityBill.RefuseAssessment(null, CollectionSourcePart.Water, false, 0m, 0m, 0m));
    }

    [Fact]
    public void Refuse_ChangesToRecordedMeterEvidence_ButNotItsUnchangedResubmission()
    {
        var bill = Metered();

        Assert.Null(UtilityBill.RefuseAssessment(bill, CollectionSourcePart.Electricity, false, 1200m, 1248m, 8m));
        Assert.NotNull(UtilityBill.RefuseAssessment(bill, CollectionSourcePart.Electricity, false, 1200m, 1249m, 8m));
        Assert.NotNull(UtilityBill.RefuseAssessment(bill, CollectionSourcePart.Electricity, false, 0m, 0m, 0m));
        Assert.Null(UtilityBill.RefuseAssessment(bill, CollectionSourcePart.Electricity, true, 0m, 0m, 0m));
    }

    [Fact]
    public void BasisFor_KeepsTheRecordedBasisOfAnUnchangedPart()
    {
        var (p, c, r) = UtilityBill.DirectApprovedReadings(10m);
        var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 9, 1200m, 1248m, 8m, p, c, r);
        bill.SetCalculationBasis(UtilityCalculationBasis.Metered, UtilityCalculationBasis.DirectApproved);

        Assert.Equal(UtilityCalculationBasis.DirectApproved,
            UtilityBill.BasisFor(bill, CollectionSourcePart.Water, false, p, c, r));
        Assert.Equal(UtilityCalculationBasis.Metered,
            UtilityBill.BasisFor(bill, CollectionSourcePart.Electricity, false, 1200m, 1248m, 8m));
        Assert.Equal(UtilityCalculationBasis.DirectApproved,
            UtilityBill.BasisFor(bill, CollectionSourcePart.Electricity, true, 0m, 1m, 400m));
    }

    [Fact]
    public void Refuse_RejectsANonUtilityPart()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UtilityBill.RefuseAssessment(null, (CollectionSourcePart)99, true, 0m, 0m, 0m));
    }
}
