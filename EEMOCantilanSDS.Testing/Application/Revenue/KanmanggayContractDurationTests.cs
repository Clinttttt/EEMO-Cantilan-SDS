using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

public sealed class KanmanggayContractDurationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(50)]
    public void RecordedTermIsFlexibleAndDoesNotCloseOrPriceTheMonthlyAccount(int years)
    {
        var start = new DateOnly(2026, 10, 1);
        var account = ObligationAccount.Create(Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental,
            Guid.Empty, null, "1", null, null, start, "Head", actualOccupant: "Recorded holder");
        account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "Signed reference");
        account.SetLeaseDetails("Recorded lessee", start, years, 9m, "Row A", 800m);
        Assert.Equal(years, account.ContractYears);
        Assert.Equal(800m, account.ContractMonthlyRate);
        Assert.Null(account.ActiveTo);
        Assert.Equal(start, account.ActiveFrom);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void RecordedTermStillRejectsUnsupportedBounds(int years)
    {
        var start = new DateOnly(2026, 10, 1);
        var account = ObligationAccount.Create(Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental,
            Guid.Empty, null, "1", null, null, start, "Head", actualOccupant: "Recorded holder");
        account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "Signed reference");
        Assert.Throws<ArgumentException>(() => account.SetLeaseDetails("Recorded lessee", start, years, null, null));
    }
}
