using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

public sealed class VegetableFruitAccountTests
{
    [Fact]
    public void MonthlySpace_HasItsOwnIdentityAndClassification_WithoutAMasterPayor()
    {
        var start = new DateOnly(2026, 10, 8);
        var account = ObligationAccount.Create(Guid.NewGuid(), ObligationKind.VegetableFruitSpaceRental,
            Guid.Empty, null, "1", null, null, start, "head", actualOccupant: "Ana Reyes");
        account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "Lease 14");
        Assert.Null(account.PayorId);
        Assert.Null(account.StallId);
        Assert.Equal("Ana Reyes", account.ActualOccupant);
        Assert.Equal(RevenueClassificationCodes.VegetableFruitSpaceRental, ObligationAccount.ClassificationCodeFor(account.Kind));
        Assert.NotEqual(ObligationAccount.ClassificationCodeFor(ObligationKind.KanmanggaySpaceRental), ObligationAccount.ClassificationCodeFor(account.Kind));
        Assert.Equal(start, account.RateAsOf(new DateOnly(2026, 10, 1)));
        account.Close(new DateOnly(2026, 11, 12));
        Assert.Equal(new[] { new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1) }, account.PeriodStarts(new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void CorrectHolderEvidence_PreservesAssessedOccupantAndContractTerm()
    {
        var start = new DateOnly(2026, 10, 1);
        var account = ObligationAccount.Create(Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental,
            Guid.Empty, null, "2", null, null, start, "Head", actualOccupant: "Pony Tail");
        account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "LEASE-2026");
        account.SetLeaseDetails("Pony Contract", start, 3, 12.5m, "Corner");
        Assert.Throws<ArgumentException>(() => account.CorrectHolderEvidence("New occupant",
            OccupancyArrangement.SignedContract, "LEASE-2026", "Pony Contract", start, 3, 12.5m,
            "Corner", hasAssessedPeriods: true));
        Assert.Throws<ArgumentException>(() => account.CorrectHolderEvidence("Pony Tail",
            OccupancyArrangement.SignedContract, "LEASE-2026", "Pony Contract", start, 5, 12.5m,
            "Corner", hasAssessedPeriods: true));
        account.CorrectHolderEvidence("Pony Tail", OccupancyArrangement.SignedContract,
            "LEASE-2026-EDIT", "Pony Contract", start, 3, 14m, "Corner A",
            hasAssessedPeriods: true);
        Assert.Equal("Pony Tail", account.ActualOccupant);
        Assert.Equal(3, account.ContractYears);
        Assert.Equal(14m, account.AreaSqm);
        Assert.Equal("Corner A", account.AreaLocation);
        Assert.Equal("LEASE-2026-EDIT", account.ContractReference);
        Assert.Equal(new[] {start}, account.PeriodStarts(start));
        account.Close(new DateOnly(2026, 10, 31));
        Assert.Throws<ArgumentException>(() => account.CorrectHolderEvidence("Pony Tail",
            OccupancyArrangement.SignedContract, "LEASE", "Pony Contract", start, 3, 14m,
            "Corner A", false));
    }

    [Fact]
    public void SpaceOnly_DoesNotCarryAnInventedContract()
    {
        var account = ObligationAccount.Create(Guid.NewGuid(), ObligationKind.VegetableFruitSpaceRental,
            Guid.Empty, null, "1", null, null, new DateOnly(2026, 10, 1), "head", actualOccupant: "Ana Reyes");
        Assert.Throws<ArgumentException>(() => account.SetOccupancyBasis(OccupancyArrangement.SpaceOnly, "invented"));
        account.SetOccupancyBasis(OccupancyArrangement.SpaceOnly, null);
        Assert.Null(account.ContractReference);
    }
}
