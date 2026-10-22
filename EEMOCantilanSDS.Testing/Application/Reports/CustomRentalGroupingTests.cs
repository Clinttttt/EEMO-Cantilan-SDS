using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Reports;

public class CustomRentalGroupingTests
{
    [Fact]
    public void CustomMonthlyRent_PreservesPendingReportRowUntilApproval()
    {
        var row = Assert.Single(OfficialMonthlyIncomeStructure.Rows, x => x.Key == "RENT_OTHER");
        Assert.Equal(OfficialMonthlyIncomeStructure.Pending, row.GroupKey);
        Assert.Equal(RevenueClassificationCodes.PermanentStallRent, row.ClassificationCode);
        Assert.Null(row.Facility);

        foreach (var code in new[] { FacilityCode.Custom1, FacilityCode.Custom2, FacilityCode.Custom3,
                                      FacilityCode.Custom4, FacilityCode.Custom5 })
            Assert.Equal("RENT_OTHER", OfficialMonthlyIncomeStructure.RowKeyFor(
                RevenueClassificationCodes.PermanentStallRent, code));
    }

    [Fact]
    public void ExistingNamedStallRentRows_DoNotGetReclassified()
    {
        Assert.Equal("RENT_NPM", OfficialMonthlyIncomeStructure.RowKeyFor(
            RevenueClassificationCodes.PermanentStallRent, FacilityCode.NPM));
        Assert.Equal("RENT_TCC", OfficialMonthlyIncomeStructure.RowKeyFor(
            RevenueClassificationCodes.PermanentStallRent, FacilityCode.TCC));
        Assert.Equal("RENT_NCC", OfficialMonthlyIncomeStructure.RowKeyFor(
            RevenueClassificationCodes.PermanentStallRent, FacilityCode.NCC));
        Assert.Equal("RENT_BBQ", OfficialMonthlyIncomeStructure.RowKeyFor(
            RevenueClassificationCodes.PermanentStallRent, FacilityCode.BBQ));
    }
}