using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Reports;

/// <summary>
/// The Monthly Collection Report's grouping and its reconciliation of account-level detail to the official source
/// projection. The official projection is the only source of money; these rules only place and compare.
/// </summary>
public class MonthlyCollectionReportTests
{
    private static RevenueSourcePerformanceRowDto Row(string key, string group, string model, decimal collected, decimal legacy = 0m) =>
        new(key, key, group, group, model, "OR", null, collected, legacy, collected - legacy, 1, null, null, "Active", false);

    private static MonthEndFacilityDto Facility(FacilityCode code, decimal collected, string? name = null) =>
        new(code, name ?? code.ToString(), true, collected, 0m, 0, 0, 0, 0, 0, [], []);

    private static RevenueSourcePerformanceDto Official(params RevenueSourcePerformanceRowDto[] rows) =>
        new(2026, 10, [], rows, rows.Sum(r => r.Collected), [], DateTime.UtcNow);

    private static MonthEndReportDto Detail(params MonthEndFacilityDto[] facilities) =>
        new(2026, 10, "October 2026", facilities.Sum(f => f.Collected), 0m, 0, 0, 0, 0, facilities);

    [Fact]
    public void EveryCatalogGroupReadsUnderOneOfTheReportsGroups_AndAnUnnamedReceivableReadsUnderOthers()
    {
        var keys = MonthlyCollectionReport.Groups.Select(g => g.Key).ToHashSet();
        foreach (var group in RevenueSourceCatalog.Groups)
            Assert.Contains(MonthlyCollectionReport.DisplayGroup(group.Key), keys);

        Assert.Equal(RevenueSourceCatalog.OtherGroup, MonthlyCollectionReport.DisplayGroup(RevenueSourceCatalog.ReceivableGroup));
        Assert.Equal("Others", MonthlyCollectionReport.GroupLabel(RevenueSourceCatalog.OtherGroup));
        Assert.Equal(
            ["Income From Market", "Rent Income — Stall Rental", "Space Rental", "Income From Terminal", "Income From Slaughterhouse", "Others"],
            MonthlyCollectionReport.Groups.Select(g => g.Label).ToArray());
    }

    [Fact]
    public void FinesReadUnderSpaceRentalAndRecoveredArrearsUnderRentIncome_LikeTheOfficesMonthlyIncomeSheet()
    {
        var fines = Row("PENALTIES_AND_FINES", RevenueSourceCatalog.OtherGroup, RevenueSourceModel.Transactional, 250m);
        var arrears = Row("ARREARS", RevenueSourceCatalog.ReceivableGroup, RevenueSourceModel.Receivable, 1200m);

        Assert.Equal(RevenueSourceCatalog.SpaceGroup, MonthlyCollectionReport.DisplayGroup(fines));
        Assert.Equal(RevenueSourceCatalog.RentGroup, MonthlyCollectionReport.DisplayGroup(arrears));
        // Presentation only: the catalog still classifies them as it always has, and every other source follows its own group.
        Assert.Equal(RevenueSourceCatalog.OtherGroup, RevenueSourceCatalog.For("PENALTIES_AND_FINES").GroupKey);
        Assert.Equal(RevenueSourceCatalog.ReceivableGroup, RevenueSourceCatalog.For("ARREARS").GroupKey);
        Assert.Equal(RevenueSourceCatalog.MarketGroup,
            MonthlyCollectionReport.DisplayGroup(Row("MARKET_FEES", RevenueSourceCatalog.MarketGroup, RevenueSourceModel.Transactional, 1m)));
        // An unknown classification and an unnamed receivable are never dropped: they still read under Others.
        Assert.Equal(RevenueSourceCatalog.OtherGroup,
            MonthlyCollectionReport.DisplayGroup(Row("OTHER_STRANGE", RevenueSourceCatalog.OtherGroup, RevenueSourceModel.Transactional, 1m)));
        Assert.Equal(RevenueSourceCatalog.OtherGroup,
            MonthlyCollectionReport.DisplayGroup(Row("SOME_RECEIVABLE", RevenueSourceCatalog.ReceivableGroup, RevenueSourceModel.Receivable, 1m)));
    }

    [Fact]
    public void ArrearsIsListedOnlyWhenItHoldsRecoveredMoney_AndAnUnknownClassificationIsNeverDropped()
    {
        var empty = Row("ARREARS", RevenueSourceCatalog.ReceivableGroup, RevenueSourceModel.Receivable, 0m);
        var recovered = Row("ARREARS", RevenueSourceCatalog.ReceivableGroup, RevenueSourceModel.Receivable, 1200m);
        var unknown = Row("OTHER_STRANGE", RevenueSourceCatalog.OtherGroup, RevenueSourceModel.Transactional, 0m);

        Assert.False(MonthlyCollectionReport.IsListed(empty));
        Assert.True(MonthlyCollectionReport.IsListed(recovered));
        Assert.True(MonthlyCollectionReport.IsListed(unknown));
        Assert.Equal(["ARREARS", "OTHER_STRANGE"], MonthlyCollectionReport.ListedRows(Official(empty, recovered, unknown)).Select(r => r.Key).ToArray());
    }

    [Fact]
    public void EveryFacilityHasAGroupAndAtLeastOneOfficialRow_AndTerminalIsNotTransportationParking()
    {
        foreach (var code in Enum.GetValues<FacilityCode>())
        {
            Assert.NotEmpty(MonthlyCollectionReport.RowKeysOf(code));
            Assert.Contains(MonthlyCollectionReport.GroupOf(code), MonthlyCollectionReport.Groups.Select(g => g.Key));
        }

        Assert.Equal(RevenueSourceCatalog.TerminalGroup, MonthlyCollectionReport.GroupOf(FacilityCode.TRM));
        Assert.DoesNotContain("TRANSPORTATION_PARKING", MonthlyCollectionReport.RowKeysOf(FacilityCode.TRM));
        Assert.Equal(RevenueSourceCatalog.MarketGroup, MonthlyCollectionReport.GroupOf(FacilityCode.ICE));
        Assert.Equal(RevenueSourceCatalog.MarketGroup, MonthlyCollectionReport.GroupOf(FacilityCode.TPM));
        Assert.Equal(RevenueSourceCatalog.RentGroup, MonthlyCollectionReport.GroupOf(FacilityCode.NPM));
        Assert.True(MonthlyCollectionReport.DetailBelongsTo(FacilityCode.SLH, "SLAUGHTERHOUSE"));
        Assert.False(MonthlyCollectionReport.DetailBelongsTo(FacilityCode.SLH, "RENT_NPM"));
    }

    [Fact]
    public void ReconciliationStatesAgreementAndDifferencesWithoutAbsorbingThem()
    {
        var official = Official(
            Row("RENT_NPM", RevenueSourceCatalog.RentGroup, RevenueSourceModel.RecurringObligation, 5000m),
            Row("RENT_TCC", RevenueSourceCatalog.RentGroup, RevenueSourceModel.RecurringObligation, 2000m),
            Row("MARKET_FEES", RevenueSourceCatalog.MarketGroup, RevenueSourceModel.Transactional, 700m),
            Row("PENALTIES_AND_FINES", RevenueSourceCatalog.OtherGroup, RevenueSourceModel.Transactional, 300m));
        var detail = Detail(Facility(FacilityCode.NPM, 5000m), Facility(FacilityCode.TCC, 1900m));

        var result = MonthlyCollectionReport.Reconcile(detail, official);

        var npm = result.Rows.Single(r => r.Label == "NPM");
        Assert.True(npm.Agrees);
        var tcc = result.Rows.Single(r => r.Label == "TCC");
        Assert.False(tcc.Agrees);
        Assert.Equal(-100m, tcc.Difference);
        Assert.False(result.AllAgree);
        // Every peso of the official headline is itemised or stated as source-total-only: 8,000 official; the 7,000 the two
        // facilities are compared against has detail, the Market Fees and Fines 1,000 are source totals only.
        Assert.Equal(8000m, result.OfficialTotal);
        Assert.Equal(7000m, result.OfficialWithDetail);
        Assert.Equal(1000m, result.WithoutDetail);
        Assert.Equal(result.OfficialTotal, result.OfficialWithDetail + result.WithoutDetail);
    }

    [Fact]
    public void LegacyTerminalTripsAreComparedOnlyWithTheLegacyPartOfTheTerminalRows()
    {
        var official = Official(
            Row("TERMINAL_COMFORT_ROOM", RevenueSourceCatalog.TerminalGroup, RevenueSourceModel.Transactional, 900m, legacy: 400m),
            Row("TERMINAL_TRICYCAD", RevenueSourceCatalog.TerminalGroup, RevenueSourceModel.Transactional, 600m, legacy: 100m),
            Row("TRANSPORTATION_PARKING", RevenueSourceCatalog.MarketGroup, RevenueSourceModel.Transactional, 250m, legacy: 250m));
        var detail = Detail(Facility(FacilityCode.TRM, 500m, "Transport Terminal"));

        var result = MonthlyCollectionReport.Reconcile(detail, official);

        var trips = Assert.Single(result.Rows);
        Assert.Equal("Transport Terminal — legacy trip records", trips.Label);
        Assert.Equal(500m, trips.Official);          // 400 + 100 legacy; the 1,500 canonical + legacy total and Transportation/Parking are not used
        Assert.True(trips.Agrees);
        // 1,750 official: only the 500 legacy part is itemised by account detail; Terminal cash since cutover and
        // Transportation/Parking are stated by their source totals.
        Assert.Equal((1750m, 500m, 1250m), (result.OfficialTotal, result.OfficialWithDetail, result.WithoutDetail));
    }

    [Fact]
    public void ConfiguredRentalFacilitiesAreComparedTogetherWithTheOtherRentRow_AndMissingOfficialRowsAreZeroNotHidden()
    {
        var official = Official(Row("RENT_OTHER", RevenueSourceCatalog.RentGroup, RevenueSourceModel.RecurringObligation, 300m));
        var detail = Detail(Facility(FacilityCode.Custom1, 100m), Facility(FacilityCode.Custom2, 200m), Facility(FacilityCode.SLH, 50m));

        var result = MonthlyCollectionReport.Reconcile(detail, official);

        var other = result.Rows.Single(r => r.Label == "Other rental facilities");
        Assert.Equal((300m, 300m), (other.Detail, other.Official));
        Assert.True(other.Agrees);
        var slaughter = result.Rows.Single(r => r.Label == "SLH");
        Assert.Equal(0m, slaughter.Official);        // no official row was returned: stated as a difference, never dropped
        Assert.False(slaughter.Agrees);
    }
}
