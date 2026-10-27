using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Reports;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class CollectionReconciliationPageTests : TestContext
{
    private readonly Mock<ICollectionActivityApiClient> api = new();
    private readonly Mock<IRemittancesApiClient> remittances = new();
    public CollectionReconciliationPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Head");
        Services.AddSingleton(api.Object); Services.AddSingleton(Mock.Of<ISetupApiClient>());
        remittances.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid?>(),
            It.IsAny<RevenueInstrumentType?>(), It.IsAny<RemittanceStatus?>()))
            .ReturnsAsync(Result<IReadOnlyList<RemittanceRowDto>>.Success([]));
        Services.AddSingleton(remittances.Object);
        Services.AddSingleton(Mock.Of<IStallsApiClient>()); Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(new BrandingState(Mock.Of<IMunicipalitiesApiClient>()));
    }
    [Theory]
    [InlineData(CollectionReconciliationBasis.LatestCorrected, 0)]
    [InlineData(CollectionReconciliationBasis.AsOriginallyKnown, 900)]
    [InlineData(CollectionReconciliationBasis.CorrectionActivity, -900)]
    public void Screen_CSV_and_print_keep_the_selected_evidence_basis(CollectionReconciliationBasis basis, decimal total)
    {
        var correction = new CollectionActivityCorrectionDto(Guid.NewGuid(), "Reversal", new(2026, 11, 1), new(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc), -900, null, "Verified reversal");
        var original = new CollectionActivityEventDto("original", "Canonical", "Collection", Guid.NewGuid(), new(2026, 10, 15), new(2026, 10, 15, 4, 0, 0, DateTimeKind.Utc),
            "SRC-2026-000900", null, null, [], [], "Synthetic vendor", null, null, null, "Head", null, null, null,
            900, basis == CollectionReconciliationBasis.AsOriginallyKnown ? 0 : -900, basis == CollectionReconciliationBasis.AsOriginallyKnown ? 900 : 0,
            "Posted", null, [], basis == CollectionReconciliationBasis.AsOriginallyKnown ? [] : [correction]);
        api.Setup(x => x.GetReconciliationAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CollectionReconciliationBasis>(), It.IsAny<DateTimeOffset?>(), 5000))
            .ReturnsAsync(Result<CollectionReconciliationDto>.Success(new(new(2026, basis == CollectionReconciliationBasis.CorrectionActivity ? 11 : 10, 1), new(2026, basis == CollectionReconciliationBasis.CorrectionActivity ? 11 : 10, basis == CollectionReconciliationBasis.CorrectionActivity ? 30 : 31), basis,
                basis == CollectionReconciliationBasis.AsOriginallyKnown ? new(2026, 10, 31, 15, 59, 59, DateTimeKind.Utc) : new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), [original], 1, false,
                basis == CollectionReconciliationBasis.CorrectionActivity ? 0 : 900, original.CorrectionEffect, total, "Reconstructed. Official RCD treatment pending Office confirmation. Legacy history excluded.")));
        var cut = RenderComponent<CollectionReconciliation>();
        // Under parallel bUnit load the asynchronous report render can outlast the
        // one-second default; keep the exact evidence assertion with a bounded wait.
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".recon-document")), TimeSpan.FromSeconds(5));
        Assert.Contains("SRC-2026-000900", cut.Markup); Assert.Contains("pending Office", cut.Markup);
        Assert.Contains(Math.Abs(total).ToString("N2"), cut.Find(".recon-total").TextContent);
        var csv = Uri.UnescapeDataString(cut.Find("a[download]").GetAttribute("href")!.Split(',', 2)[1]);
        Assert.Contains("Knowledge cutoff UTC", csv); Assert.Contains("SRC-2026-000900", csv);
        Assert.Contains(total.ToString("N2", System.Globalization.CultureInfo.InvariantCulture), csv);
        cut.Find("button[aria-label='Print']").Click();
        cut.WaitForAssertion(() => Assert.Single(JSInterop.Invocations.Where(x => x.Identifier == "stalltrackPrint.landscape")));
        var directory = Path.Combine(Path.GetTempPath(), "stalltrack-rcd-review"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, basis + ".html"), cut.Markup);
        File.WriteAllText(Path.Combine(directory, basis + ".csv"), csv);
    }
    private static CollectionActivityEventDto Slaughter(string src, string payer, params decimal[] charges) => new("k" + src, "Canonical", "Collection", Guid.NewGuid(), new(2026, 10, 19),
        new(2026, 10, 19, 1, 20, 0, DateTimeKind.Utc), src, null, null, [], [], payer, null, null, null, "Head", null, null, null,
        charges.Sum(), 0, charges.Sum(), "Posted", null,
        charges.Select((c, i) => new CollectionActivityLineDto("SLAUGHTERHOUSE", "Slaughter charge " + i, c, "GovernedService", null, Guid.NewGuid(), null, null, "Charge " + i, "Slaughterhouse")).ToList(), []);

    private void Serve(params CollectionActivityEventDto[] events) =>
        api.Setup(x => x.GetReconciliationAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CollectionReconciliationBasis>(), It.IsAny<DateTimeOffset?>(), 5000))
            .ReturnsAsync(Result<CollectionReconciliationDto>.Success(new(new(2026, 10, 1), new(2026, 10, 31), CollectionReconciliationBasis.LatestCorrected, DateTime.UtcNow,
                events, events.Length, false, events.Sum(e => e.Amount), 0, events.Sum(e => e.Amount), "Reconstructed. Pending Office confirmation.")));

    [Fact]
    public void OneRowPerSrc_ShowsTheParentRevenueLine_NotTheChargeNames_AndTheDateAndTimeApart()
    {
        Serve(Slaughter("SRC-2026-000021", "Ana Reyes", 100m, 50m, 20m));
        var cut = RenderComponent<CollectionReconciliation>();
        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tbody tr"));
            Assert.Equal(7, cut.FindAll("thead th").Count);
            Assert.Contains("SRC-2026-000021", row.TextContent); Assert.Contains("Ana Reyes", row.TextContent);
            Assert.Contains("Slaughterhouse", row.TextContent);
            Assert.DoesNotContain("Charge 0", row.TextContent); Assert.DoesNotContain("Slaughter charge", row.TextContent);
            Assert.Contains("₱170.00", row.TextContent);
            Assert.Equal("Oct 19, 2026", row.QuerySelectorAll("td")[2].FirstChild!.TextContent.Trim());     // 01:20 UTC = 09:20 AM PHT
            Assert.Equal("09:20 AM", cut.Find(".recon-time").TextContent.Trim());
            Assert.DoesNotContain("PHT", row.TextContent);
            Assert.Empty(cut.FindAll("details summary").Where(d => d.TextContent.Contains("Source and correction")));
        }, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Filters_NarrowTheRows_StateTheirTotal_AndTheCsvCarriesTheFilter()
    {
        Serve(Slaughter("SRC-2026-000021", "Ana Reyes", 170m), Slaughter("SRC-2026-000022", "Ben Cruz", 30m));
        var cut = RenderComponent<CollectionReconciliation>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        cut.Find("button[aria-label='Revenue source']").Click();
        Assert.Contains(cut.FindAll(".fh-dd-item"), o => o.TextContent.Trim() == "Slaughterhouse");
        cut.Find(".fh-dd-backdrop").Click();

        cut.Find("input[aria-label='Search SRC or payer']").Input("ben");
        Assert.Contains("SRC-2026-000022", Assert.Single(cut.FindAll("tbody tr")).TextContent);
        Assert.Contains("₱30.00", cut.Find(".recon-total").TextContent);
        Assert.Contains("whole report is ₱200.00", cut.Find(".recon-filter").TextContent);
        var csv = Uri.UnescapeDataString(cut.Find("a[download]").GetAttribute("href")!.Split(',', 2)[1]);
        Assert.Contains("Filter,", csv.Replace("\"", "")); Assert.Contains("Report-wide total", csv); Assert.DoesNotContain("SRC-2026-000021", csv);

        cut.Find("input[aria-label='Search SRC or payer']").Input("zzz");
        Assert.Contains("No transaction matches", cut.Markup);
    }

    [Fact]
    public void ViewDetails_OpensAModal_WithTheChargesAndReconciliation_AndNoRawGuids()
    {
        var e = Slaughter("SRC-2026-000021", "Ana Reyes", 100m, 50m);
        Serve(e);
        var cut = RenderComponent<CollectionReconciliation>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("tbody tr")));
        cut.Find("button[aria-label='View details of SRC-2026-000021']").Click();
        var dialog = cut.Find("[role='dialog']");
        Assert.Contains("Charge 0", dialog.TextContent); Assert.Contains("Charge 1", dialog.TextContent);
        Assert.Contains("Resulting net", dialog.TextContent); Assert.Contains("₱150.00", dialog.TextContent);
        Assert.Contains("Not recorded", dialog.TextContent);                                      // no document number is invented
        Assert.DoesNotMatch(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", dialog.TextContent);
        dialog.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("[role='dialog']"));
    }

    [Fact]
    public void Truncated_reconciliation_cannot_be_printed_or_exported_as_a_complete_statement()
    {
        api.Setup(x => x.GetReconciliationAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CollectionReconciliationBasis>(), It.IsAny<DateTimeOffset?>(), 5000))
            .ReturnsAsync(Result<CollectionReconciliationDto>.Success(new(new(2026, 10, 1), new(2026, 10, 31), CollectionReconciliationBasis.LatestCorrected,
                DateTime.UtcNow, [], 5001, true, 900, 0, 900, "Reconstructed")));
        var cut = RenderComponent<CollectionReconciliation>();
        cut.WaitForAssertion(() => Assert.Contains("Totals cover all matches", cut.Markup));
        Assert.Empty(cut.FindAll("a[download]"));
        Assert.Empty(cut.FindAll("button[aria-label='Print']"));
    }
    [Fact]
    public void RevisionEvidence_ReadsInOfficeTerms_WithNoStoredKeysOrIdentifiersOnScreen()
    {
        var official = new Mock<IOfficialReportsApiClient>(); var first = Guid.NewGuid();
        official.Setup(x => x.GetGovernanceAsync(It.IsAny<int>())).ReturnsAsync(Result<IReadOnlyList<ReportRevisionDto>>.Success([
            new(first, OfficialReportRevisionKind.AnnualTarget, "FISH_MEAT_VENDOR_FEE", 2026, 0, 1, null, 799666m, null, "MEEDO", null, null, Guid.NewGuid(), "admin", new(2026, 10, 10, 14, 46, 30, DateTimeKind.Utc)),
            new(Guid.NewGuid(), OfficialReportRevisionKind.AnnualTarget, "FISH_MEAT_VENDOR_FEE", 2026, 0, 2, first, 800000m, null, "MEEDO", "Resolution 5", null, Guid.NewGuid(), "admin", new(2026, 10, 11, 1, 0, 0, DateTimeKind.Utc))]));
        Services.AddSingleton(official.Object);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/reports/monthly-income/history?year=2026");
        var cut = RenderComponent<MonthlyIncomeHistory>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        var rows = cut.Find("tbody").TextContent;
        Assert.DoesNotContain("FISH_MEAT_VENDOR_FEE", rows); Assert.DoesNotContain("AnnualTarget", rows);
        Assert.DoesNotMatch(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", cut.Find("table").TextContent);
        Assert.Contains("Annual target", rows); Assert.Contains("Supersedes revision 1", rows); Assert.Contains("First entry", rows);
        Assert.Contains("10:46 PM", rows);                                                        // 14:46 UTC = 22:46 PHT, time apart from the date
        Assert.DoesNotContain("PHT", rows);
        Assert.Contains("₱799,666.00", rows);
        cut.Find("input[aria-label='Search revisions']").Input("resolution");
        Assert.Single(cut.FindAll("tbody tr"));                                                    // only revision 2 carries that reference
        Assert.Contains("Showing 1 of 2", cut.Find(".rv-filter").TextContent);
    }

    [Fact]
    public void Monthly_income_history_keeps_captured_system_and_both_revisions_visible()
    {
        var official = new Mock<IOfficialReportsApiClient>(); var first = Guid.NewGuid();
        ReportRevisionDto Revision(Guid id, int version, Guid? supersedes, decimal amount) => new(id, OfficialReportRevisionKind.MonthlyAdjustment, "RENT_NPM", 2026, 10, version, supersedes,
            amount, 900, "Office reason", "Evidence reference", null, Guid.NewGuid(), "Head", new(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc));
        official.Setup(x => x.GetGovernanceAsync(It.IsAny<int>())).ReturnsAsync(Result<IReadOnlyList<ReportRevisionDto>>.Success([
            Revision(first, 1, null, -50), Revision(Guid.NewGuid(), 2, first, -25)]));
        Services.AddSingleton(official.Object);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/reports/monthly-income/history?year=2026");
        var cut = RenderComponent<MonthlyIncomeHistory>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        Assert.Contains("900.00", cut.Markup); Assert.Contains("850.00", cut.Markup); Assert.Contains("875.00", cut.Markup);
        Assert.Contains("not persisted copies", cut.Markup);
        var csv = Uri.UnescapeDataString(cut.Find("a[download]").GetAttribute("href")!.Split(',', 2)[1]);
        Assert.Contains(first.ToString(), csv); Assert.Contains("850.00", csv); Assert.Contains("875.00", csv);
        official.Verify(x => x.AdjustMonthlyIncomeAsync(It.IsAny<SetMonthlyIncomeAdjustmentRequest>()), Times.Never);
    }

    [Fact]
    public void RevisionEvidence_ListsRecordedRemittanceWithDifferenceAndRealActor_WithoutChangingReportRevisions()
    {
        var official = new Mock<IOfficialReportsApiClient>();
        var revisionId = Guid.NewGuid();
        var remittanceId = Guid.NewGuid();
        official.Setup(x => x.GetGovernanceAsync(2026)).ReturnsAsync(Result<IReadOnlyList<ReportRevisionDto>>.Success([
            new(revisionId, OfficialReportRevisionKind.AnnualTarget, "RENT_NPM", 2026, 0, 1, null, 500m, null,
                "Approved budget", null, null, Guid.NewGuid(), "Head", new(2026, 10, 2, 2, 0, 0, DateTimeKind.Utc))]));
        remittances.Setup(x => x.GetRegisterAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null, null, null))
            .ReturnsAsync(Result<IReadOnlyList<RemittanceRowDto>>.Success([
                new(remittanceId, new(2026, 10, 28), Guid.NewGuid(), "Ana Reyes", null, 2,
                    67m, 65m, 2m, "Office 4", RemittanceStatus.Recorded,
                    new(2026, 10, 28, 2, 0, 0, DateTimeKind.Utc), RecordedBy: "Head Bobby",
                    Remarks: "Cash counted at turnover")]));
        Services.AddSingleton(official.Object);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/reports/monthly-income/history?year=2026");

        var cut = RenderComponent<MonthlyIncomeHistory>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count), TimeSpan.FromSeconds(5));
        var remittance = cut.FindAll("tbody tr").Single(x => x.TextContent.Contains("Ana Reyes"));
        Assert.Contains("Recorded", remittance.TextContent);
        Assert.Contains("Difference ₱2.00", remittance.TextContent);
        Assert.Contains("Head Bobby", remittance.TextContent);
        Assert.Contains("Cash counted at turnover", remittance.TextContent);
        Assert.Equal($"/remittances/{remittanceId}", remittance.QuerySelector("a")!.GetAttribute("href"));
        Assert.DoesNotContain("Needs review", remittance.TextContent);
        Assert.DoesNotMatch(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", cut.Find("table").TextContent);
        var csv = Uri.UnescapeDataString(cut.Find("a[download]").GetAttribute("href")!.Split(',', 2)[1]);
        Assert.Contains(remittanceId.ToString(), csv);
        Assert.Contains(revisionId.ToString(), csv);
        Assert.Contains("2.00", csv);

        cut.Find("button[aria-label='Record type']").Click();
        cut.FindAll("[role='option']").Single(x => x.TextContent.Trim() == "Remittance").Click();
        Assert.Single(cut.FindAll("tbody tr"));
        Assert.Contains("Showing 1 of 2", cut.Find(".rv-filter").TextContent);
        cut.Find("input[aria-label='Search revisions']").Input("not a matching actor");
        Assert.Empty(cut.FindAll("tbody tr"));
        official.Verify(x => x.AdjustMonthlyIncomeAsync(It.IsAny<SetMonthlyIncomeAdjustmentRequest>()), Times.Never);
    }

    [Fact]
    public void RevisionEvidence_DoesNotPresentPartialYear_WhenRemittanceReaderFails()
    {
        var official = new Mock<IOfficialReportsApiClient>();
        official.Setup(x => x.GetGovernanceAsync(It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<ReportRevisionDto>>.Success([
                new(Guid.NewGuid(), OfficialReportRevisionKind.AnnualTarget, "RENT_NPM", 2026, 0, 1, null,
                    500m, null, "Approved", null, null, Guid.NewGuid(), "Head", DateTime.UtcNow)]));
        remittances.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, null, null))
            .ReturnsAsync(Result<IReadOnlyList<RemittanceRowDto>>.Failure("Remittance evidence unavailable", 500));
        Services.AddSingleton(official.Object);
        var cut = RenderComponent<MonthlyIncomeHistory>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[role='alert']")), TimeSpan.FromSeconds(5));
        Assert.Empty(cut.FindAll("a[download]"));
        Assert.Empty(cut.FindAll("tbody tr"));
    }
}
