using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Regression tests for the server-aggregated TPM collection history
/// (<see cref="TpmRepository.GetHistoryAsync"/>), which powers the report's Yearly and History phases.
/// MarketDate is a calendar <see cref="DateOnly"/> Friday, so a past year can be seeded deterministically.
/// </summary>
public class TpmHistoryTests : RepositoryTestBase
{
    // Fridays used for seeding (all verified Fridays in 2024).
    private static readonly DateOnly Jan05 = new(2024, 1, 5);
    private static readonly DateOnly Jan12 = new(2024, 1, 12);
    private static readonly DateOnly Feb02 = new(2024, 2, 2);

    [Fact]
    public async Task GetHistory_AggregatesAttendanceByPaymentMarketDayAndGoods()
    {
        var context = NewContext();

        var veggieA = TpmVendor.Create("Vendor A", "Vegetables");
        var fishB = TpmVendor.Create("Vendor B", "Fish");
        var veggieC = TpmVendor.Create("Vendor C", "Vegetables");
        context.TpmVendors.AddRange(veggieA, fishB, veggieC);

        // Jan 5: A paid, B unpaid. Jan 12: C paid. Feb 2: A paid.
        context.TpmAttendances.AddRange(
            Paid(veggieA.Id, Jan05),
            Unpaid(fishB.Id, Jan05),
            Paid(veggieC.Id, Jan12),
            Paid(veggieA.Id, Feb02));
        await context.SaveChangesAsync();

        var repo = new TpmRepository(context);
        var history = await repo.GetHistoryAsync(2024, CancellationToken.None);

        Assert.Equal(2024, history.Year);
        Assert.Equal(5, history.Yearly.Count);          // rolling 5-year window
        Assert.Equal(12, history.Monthly.Count);        // past year → all 12 months in scope

        // ── Yearly 2024 totals ──
        var y = history.Yearly.Single(r => r.Year == 2024);
        Assert.Equal(3, y.MarketDays);                  // 3 distinct Fridays
        Assert.Equal(4, y.VendorEntries);
        Assert.Equal(3, y.PaidEntries);
        Assert.Equal(1, y.UnpaidEntries);
        Assert.Equal(3 * FeeRates.TpmVendorFee, y.Collected);
        Assert.Equal(75, y.CollectionRate);             // 3 of 4

        // Goods tally (ordered by entries desc): Vegetables 3 (₱300), Fish 1 (₱0 — unpaid).
        Assert.Equal("Vegetables", y.Goods[0].Goods);
        Assert.Equal(3, y.Goods[0].Entries);
        Assert.Equal(3 * FeeRates.TpmVendorFee, y.Goods[0].Collected);
        Assert.Contains(y.Goods, g => g.Goods == "Fish" && g.Entries == 1 && g.Collected == 0m);

        // ── January row ──
        var jan = history.Monthly.Single(m => m.Month == 1);
        Assert.Equal(2, jan.MarketDays);
        Assert.Equal(3, jan.VendorEntries);
        Assert.Equal(2, jan.PaidEntries);
        Assert.Equal(2 * FeeRates.TpmVendorFee, jan.Collected);
        Assert.Equal(67, jan.CollectionRate);           // round(2/3)

        // ── February row ──
        var feb = history.Monthly.Single(m => m.Month == 2);
        Assert.Equal(1, feb.MarketDays);
        Assert.Equal(1, feb.PaidEntries);
        Assert.Equal(100, feb.CollectionRate);
    }

    [Fact]
    public async Task GetHistory_FutureYear_ReturnsNoMonthlyRows()
    {
        var context = NewContext();
        var repo = new TpmRepository(context);

        var history = await repo.GetHistoryAsync(2999, CancellationToken.None);

        Assert.Empty(history.Monthly);
        Assert.Equal(5, history.Yearly.Count);
        Assert.All(history.Yearly, y => Assert.Equal(0, y.VendorEntries));
    }

    private static TpmAttendance Paid(Guid vendorId, DateOnly date)
    {
        var att = TpmAttendance.Create(vendorId, date);
        att.MarkPaid(null);
        return att;
    }

    private static TpmAttendance Unpaid(Guid vendorId, DateOnly date)
        => TpmAttendance.Create(vendorId, date);
}
