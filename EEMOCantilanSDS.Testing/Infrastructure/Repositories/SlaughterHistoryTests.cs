using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Regression tests for the server-aggregated slaughterhouse collection history
/// (<see cref="SlaughterRepository.GetHistoryAsync"/>), which powers the SLH report's
/// Yearly and History phases.
/// </summary>
public class SlaughterHistoryTests : RepositoryTestBase
{
    [Fact]
    public async Task GetHistory_AggregatesMonthlyAndYearly_ByAnimalTypeOwnerAndAmount()
    {
        var context = NewContext();
        var fac = Guid.NewGuid();

        // Past year so all 12 months are in scope deterministically (independent of "today").
        var hog      = SlaughterTransaction.CreateHog(fac, null, "Owner A", 2, "OR-1", new DateOnly(2024, 1, 10));
        var goat     = SlaughterTransaction.CreateCustomAnimal(fac, null, "Owner B", "Goat", 1, 100m, "OR-2", new DateOnly(2024, 1, 15));
        var carabao  = SlaughterTransaction.CreateLargeAnimal(fac, null, "Owner A", AnimalType.Carabao, 1, "OR-3", new DateOnly(2024, 3, 5));

        context.SlaughterTransactions.AddRange(hog, goat, carabao);
        await context.SaveChangesAsync();

        var repo = new SlaughterRepository(context);
        var history = await repo.GetHistoryAsync(2024, CancellationToken.None);

        Assert.Equal(2024, history.Year);
        Assert.Equal(12, history.Monthly.Count);   // a past year → all 12 months
        Assert.Equal(5, history.Yearly.Count);      // rolling 5-year window 2020..2024

        // January: hog (2 heads, Owner A) + goat (1 head, Owner B)
        var jan = history.Monthly.Single(m => m.Month == 1);
        Assert.Equal(2, jan.Transactions);
        Assert.Equal(2, jan.Receipts);              // OR-1 + OR-2
        Assert.Equal(2, jan.OwnersServed);          // Owner A + Owner B
        Assert.Equal(3, jan.TotalHeads);
        Assert.Equal(2, jan.HogHeads);
        Assert.Equal(1, jan.OtherHeads);
        Assert.Equal(0, jan.CarabaoHeads);
        Assert.Equal(hog.TotalAmount + 100m, jan.TotalCollected);
        Assert.Equal(100m, jan.OtherRevenue);

        // February: empty
        var feb = history.Monthly.Single(m => m.Month == 2);
        Assert.Equal(0, feb.Transactions);
        Assert.Equal(0m, feb.TotalCollected);

        // March: carabao (1 head, Owner A)
        var mar = history.Monthly.Single(m => m.Month == 3);
        Assert.Equal(1, mar.Transactions);
        Assert.Equal(1, mar.CarabaoHeads);
        Assert.Equal(carabao.TotalAmount, mar.TotalCollected);

        // Yearly 2024 row aggregates the whole year; Owner A appears in Jan + Mar but counts once.
        var y2024 = history.Yearly.Single(y => y.Year == 2024);
        Assert.Equal(3, y2024.Transactions);
        Assert.Equal(3, y2024.Receipts);            // OR-1, OR-2, OR-3
        Assert.Equal(2, y2024.OwnersServed);
        Assert.Equal(4, y2024.TotalHeads);
        Assert.Equal(2, y2024.HogHeads);
        Assert.Equal(1, y2024.CarabaoHeads);
        Assert.Equal(1, y2024.OtherHeads);
        Assert.Equal(hog.TotalAmount + carabao.TotalAmount + 100m, y2024.TotalCollected);
    }

    [Fact]
    public async Task GetHistory_FutureYear_ReturnsNoMonthlyRows()
    {
        var context = NewContext();
        var repo = new SlaughterRepository(context);

        // A year well in the future has no started months → no monthly rows, but still 5 yearly slots.
        var history = await repo.GetHistoryAsync(2999, CancellationToken.None);

        Assert.Empty(history.Monthly);
        Assert.Equal(5, history.Yearly.Count);
        Assert.All(history.Yearly, y => Assert.Equal(0, y.Transactions));
    }
}
