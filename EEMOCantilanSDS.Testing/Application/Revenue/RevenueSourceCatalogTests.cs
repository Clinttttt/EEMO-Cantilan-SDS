using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;

namespace EEMOCantilanSDS.UnitTest.Application.Revenue;

/// <summary>
/// The revenue-source register covers every line the official statement knows, each with a deliberate model, so a new
/// statement row cannot quietly arrive in the register as an unclassified "other operation".
/// </summary>
public class RevenueSourceCatalogTests
{
    [Fact]
    public void EveryOfficialStatementRow_HasAnExplicitSourceModel()
    {
        var missing = OfficialMonthlyIncomeStructure.Rows.Select(r => r.Key).Where(k => !RevenueSourceCatalog.Knows(k)).ToList();
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("LANDING_BERTHING", RevenueSourceModel.Transactional)]
    [InlineData("MARKET_FEES", RevenueSourceModel.Transactional)]
    [InlineData("TRANSPORTATION_PARKING", RevenueSourceModel.Transactional)]
    [InlineData("RENT_NPM", RevenueSourceModel.RecurringObligation)]
    [InlineData("ICE_PLANT", RevenueSourceModel.RecurringObligation)]
    [InlineData("KANMANGGAY_SPACE_RENTAL", RevenueSourceModel.RecurringObligation)]
    [InlineData("WEIGHT_AND_MEASURE", RevenueSourceModel.QuantityService)]
    [InlineData("SLAUGHTERHOUSE", RevenueSourceModel.QuantityService)]
    [InlineData("FIESTA_ARAW_LOT_RENTAL", RevenueSourceModel.EventRental)]
    [InlineData("ARREARS", RevenueSourceModel.Receivable)]
    public void EachSource_IsDescribedByItsOwnModel(string key, string model) =>
        Assert.Equal(model, RevenueSourceCatalog.For(key).Model);

    [Fact]
    public void APaidOnServiceSource_IsNeverStatedAsBilled()
    {
        Assert.Equal("Nothing recorded", RevenueSourceCatalog.StatusFor(RevenueSourceModel.Transactional, 0m, false));
        Assert.Equal("Active", RevenueSourceCatalog.StatusFor(RevenueSourceModel.Transactional, 100m, false));
        Assert.Equal("Needs official placement", RevenueSourceCatalog.StatusFor(RevenueSourceModel.QuantityService, 50m, true));
        Assert.Equal("See Receivables", RevenueSourceCatalog.StatusFor(RevenueSourceModel.Receivable, 0m, false));
    }
}
