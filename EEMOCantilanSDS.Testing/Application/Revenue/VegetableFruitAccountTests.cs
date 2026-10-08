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
    public void SpaceOnly_DoesNotCarryAnInventedContract()
    {
        var account = ObligationAccount.Create(Guid.NewGuid(), ObligationKind.VegetableFruitSpaceRental,
            Guid.Empty, null, "1", null, null, new DateOnly(2026, 10, 1), "head", actualOccupant: "Ana Reyes");
        Assert.Throws<ArgumentException>(() => account.SetOccupancyBasis(OccupancyArrangement.SpaceOnly, "invented"));
        account.SetOccupancyBasis(OccupancyArrangement.SpaceOnly, null);
        Assert.Null(account.ContractReference);
    }
}
