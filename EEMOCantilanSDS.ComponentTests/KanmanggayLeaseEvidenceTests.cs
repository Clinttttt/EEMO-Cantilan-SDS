using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Application.Dtos.Revenue;

namespace EEMOCantilanSDS.ComponentTests;

public sealed class KanmanggayLeaseEvidenceTests
{
    private static ObligationAccount NewSpace(ObligationKind kind = ObligationKind.KanmanggaySpaceRental)
    {
        return ObligationAccount.Create(Guid.NewGuid(), kind, Guid.Empty, null, "2",
            null, null, new DateOnly(2026, 10, 1), "head", actualOccupant: "Pony Tail");
    }

    [Fact]
    public void SignedKanmanggay_RecordsRealEvidence_ButLeavesMonthlyAssessmentAndClosureUntouched()
    {
        var account = NewSpace();
        account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "KM-2026");
        var originalPeriods = account.PeriodStarts(new DateOnly(2026, 12, 1));
        account.SetLeaseDetails("Contract Lessee", new DateOnly(2026, 10, 9), 3, 4.80m, "Corner");

        Assert.Equal("Contract Lessee", account.ContractName);
        Assert.Equal(3, account.ContractYears);
        Assert.Equal(new DateOnly(2026, 10, 9), account.ContractEffectiveDate);
        Assert.Equal(4.80m, account.AreaSqm);
        Assert.Equal("Corner", account.AreaLocation);
        Assert.Equal(originalPeriods, account.PeriodStarts(new DateOnly(2026, 12, 1)));
        Assert.Null(account.ActiveTo);
    }

    [Fact]
    public void SignedKanmanggay_RejectsIncorrectTermAndDateOutsideBillingMonth()
    {
        var account = NewSpace();
        account.SetOccupancyBasis(OccupancyArrangement.SignedContract, null);
        Assert.Throws<ArgumentException>(() => account.SetLeaseDetails("A", new DateOnly(2026, 10, 9), 2, null, null));
        Assert.Throws<ArgumentException>(() => account.SetLeaseDetails("A", new DateOnly(2026, 9, 9), 3, null, null));
        Assert.Throws<ArgumentException>(() => account.SetLeaseDetails("A", new DateOnly(2026, 10, 9), 3, -1, null));
        Assert.Null(account.ContractYears);
    }

    [Fact]
    public void HistoricalSignedLease_WithoutDocumentedTerm_RemainsUnknown()
    {
        var account = NewSpace();
        account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "Historical reference");
        account.SetLeaseDetails(null, null, null, null, null);

        Assert.Null(account.ContractYears);
        Assert.Null(account.ContractEffectiveDate);
        Assert.Equal("Historical reference", account.ContractReference);
    }

    [Fact]
    public void SpaceOnly_RetainsPhysicalSpaceNotesButNotFakeSignedEvidence()
    {
        var account = NewSpace();
        account.SetOccupancyBasis(OccupancyArrangement.SpaceOnly, null);
        account.SetLeaseDetails(null, null, null, 2.5m, "Market extension");
        Assert.Equal(2.5m, account.AreaSqm);
        Assert.Null(account.ContractYears);
        Assert.Null(account.ContractName);
        Assert.Throws<ArgumentException>(() => account.SetLeaseDetails("Signed person", null, 3, null, null));
    }

    [Fact]
    public void VegetableFruit_DoesNotInheritAutomaticThreeYearRule()
    {
        var account = NewSpace(ObligationKind.VegetableFruitSpaceRental);
        account.SetOccupancyBasis(OccupancyArrangement.SignedContract, null);
        account.SetLeaseDetails("Owner on contract", new DateOnly(2026, 10, 1), null, null, null);
        Assert.Null(account.ContractYears);
    }

    [Fact]
    public void LegacyRequest_RemainsSourceCompatibleWithOptionalMetadata()
    {
        var request = new CreateObligationAccountRequest(
            ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "", null, null,
            new DateOnly(2026, 10, 1), 1000m,
            OccupancyArrangement.SignedContract, null, "Pony Tail");
        Assert.Null(request.ContractName);
        Assert.Null(request.ContractEffectiveDate);
        Assert.Null(request.ContractYears);
        Assert.Null(request.AreaSqm);
        Assert.Null(request.AreaLocation);
    }
}
