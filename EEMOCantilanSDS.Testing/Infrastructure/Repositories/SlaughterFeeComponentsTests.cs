using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The slaughter read model itemizes the components stored with each transaction. It is presentation only: the charged
/// rate and total are unchanged, and a component that does not apply to the animal stays absent.
/// </summary>
public class SlaughterFeeComponentsTests : RepositoryTestBase
{
    [Fact]
    public async Task MonthlyTransactions_CarryTheStoredComponents_WithoutChangingTheChargedTotal()
    {
        var context = NewContext();
        var fac = Guid.NewGuid();
        var hog = SlaughterTransaction.CreateHog(fac, null, "Owner A", 2, "OR-1", new DateOnly(2026, 8, 3));
        var cow = SlaughterTransaction.CreateLargeAnimal(fac, null, "Owner B", AnimalType.Cow, 1, "OR-2", new DateOnly(2026, 8, 4));
        context.SlaughterTransactions.AddRange(hog, cow);
        await context.SaveChangesAsync();

        var rows = await new SlaughterRepository(context).GetTransactionsByMonthAsync(2026, 8);

        var hogRow = rows.Single(x => x.AnimalType == AnimalType.Hog);
        Assert.Equal(hog.TotalAmount, hogRow.TotalAmount);
        Assert.Equal(hog.SlaughterFee, hogRow.Components!.SlaughterFee);
        Assert.Equal(hog.EntranceFee, hogRow.Components.EntranceFee);
        Assert.Null(hogRow.Components.SlaughterPermit);
        Assert.Null(hogRow.Components.LivestockFee);

        var cowRow = rows.Single(x => x.AnimalType == AnimalType.Cow);
        Assert.Equal(cow.SlaughterPermit, cowRow.Components!.SlaughterPermit);
        Assert.Equal(cow.PostmortemFee, cowRow.Components.PostmortemFee);
        Assert.Equal(cow.LivestockFee, cowRow.Components.LivestockFee);
        Assert.Null(cowRow.Components.EntranceFee);
    }
}
