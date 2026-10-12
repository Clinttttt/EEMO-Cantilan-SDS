using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// ECF and WCF share one V3 utility workspace (compact header, a server-filtered and paged source register, a contextual source panel,
/// compact collection activity). These tests pin what it must keep true: identity and instrument, no nested main landmark, the server's
/// status and figures shown as given (no invented balance for a direct source), preparation that is review-then-save and never a
/// collection, and the ECF / WCF specifics each page adds.
/// </summary>
public sealed class UtilityWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public UtilityWorkspaceTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ── ECF ──

    [Fact]
    public void Ecf_ShowsItsIdentityAndOfficialReceipt_InsideTheLayoutMain_WithNoPermanentCollectionPanel()
    {
        var register = Register(Row("Juan Dela Cruz", "Prepared", approved: 100m, collected: 20m, outstanding: 80m));
        Services.AddSingleton(register.Object);
        Services.AddSingleton(EcfApi().Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            Assert.Contains("Electricity Consumption Fees", Assert.Single(cut.FindAll("h1")).TextContent);
            Assert.Contains("Official Receipt", cut.Find("header").TextContent);
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/operations/ecf/statements");
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/operations/ecf/report");
            Assert.DoesNotContain("Current collection", cut.Markup);                    // no permanent side panel
            Assert.Contains("Juan Dela Cruz", cut.Find("tbody").TextContent);
        }, Timeout);
    }

    [Fact]
    public void Ecf_ShowsTheUnfinishedCollectionNotice_OnlyWhenAServerDraftExists()
    {
        Services.AddSingleton(Register(Row("Juan Dela Cruz", "Prepared", 100m, 0m, 100m)).Object);
        Services.AddSingleton(EcfApi().Object);
        var none = RenderComponent<ElectricityConsumptionFees>();
        none.WaitForAssertion(() => Assert.Contains("Juan Dela Cruz", none.Markup), Timeout);
        Assert.DoesNotContain("Unfinished collection", none.Markup);

        var withDraft = new TestContext();
        withDraft.JSInterop.Mode = JSRuntimeMode.Loose;
        withDraft.Services.AddSingleton(Mock.Of<ISetupApiClient>());
        withDraft.Services.AddSingleton(Mock.Of<IStallsApiClient>());
        withDraft.Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        withDraft.Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        withDraft.Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        withDraft.Services.AddSingleton(Register(Row("Juan Dela Cruz", "Prepared", 100m, 0m, 100m)).Object);
        withDraft.Services.AddSingleton(EcfApi(Draft()).Object);
        var cut = withDraft.RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var notice = cut.Find(".ecf-draft");
            Assert.Contains("Unfinished collection", notice.TextContent);
            Assert.Contains(notice.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Resume");
            Assert.Contains(notice.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Discard");
        }, Timeout);
    }

    [Fact]
    public void Ecf_PreparedCanonicalSource_OffersRecordPayment_AndDirectSourceOnlyStatesMobile()
    {
        Services.AddSingleton(Register(
            Row("Prepared Payer", "Prepared", 100m, 0m, 100m),
            Row("Direct Payer", "Direct", null, 50m, 0m)).Object);
        Services.AddSingleton(EcfApi().Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();
        cut.WaitForAssertion(() => Assert.Contains("Prepared Payer", cut.Markup), Timeout);

        cut.FindAll(".uw-row-btn").Single(b => b.TextContent.Contains("Prepared Payer")).Click();
        Assert.Contains(cut.FindAll(".uw-detail button"), b => b.TextContent.Trim() == "Record payment");

        cut.FindAll(".uw-row-btn").Single(b => b.TextContent.Contains("Direct Payer")).Click();
        var detail = cut.Find(".uw-detail").TextContent;
        Assert.Contains("recorded by Collectors on Mobile", detail);
        Assert.DoesNotContain(cut.FindAll(".uw-detail button"), b => b.TextContent.Trim() == "Record payment");
    }

    [Fact]
    public void Ecf_ActivityLinksEachCollectionToItsRecord()
    {
        Services.AddSingleton(Register().Object);
        var api = EcfApi();
        api.Setup(x => x.GetActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfCollectionActivityDto>>.Success(
            [
                new EcfCollectionActivityDto(Guid.NewGuid(), new DateOnly(2026, 10, 1), DateTime.UtcNow, "SRC-001", "Ana Reyes", 80m, 1, "Posted", [])
            ]));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/collections/activity?src=SRC-001"), Timeout);
    }

    // ── The shared register ──

    [Fact]
    public void Register_ShowsServerStatusAndFigures_AndNeverInventsABalanceForADirectSource()
    {
        Services.AddSingleton(Register(
            Row("Unprepared Payer", "None", null, 0m, 0m, status: "Unprepared"),
            Row("Direct Payer", "Direct", null, 75m, 0m, status: "Direct"),
            Row("Partial Payer", "Prepared", 100m, 40m, 60m, status: "Partial")).Object);
        Services.AddSingleton(EcfApi().Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr.uw-row");
            Assert.Equal(3, rows.Count);
            var direct = rows.Single(r => r.TextContent.Contains("Direct Payer"));
            Assert.Contains("Direct", direct.TextContent);
            Assert.Contains("₱75.00", direct.TextContent);
            var cells = direct.QuerySelectorAll("td");
            Assert.Equal("—", cells[1].TextContent.Trim());                          // no approved amount
            Assert.Equal("—", cells[3].TextContent.Trim());                          // no outstanding balance
            Assert.Contains("Unprepared", rows.Single(r => r.TextContent.Contains("Unprepared Payer")).TextContent);
            Assert.Contains("₱60.00", rows.Single(r => r.TextContent.Contains("Partial Payer")).TextContent);
            // The figure is net settlement applied to the billing month - never described as cash received in the period.
            Assert.Contains("Net settled", cut.Find("dl.v3h-figures").TextContent);
            Assert.DoesNotContain("Received this period", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("th"), h => h.TextContent.Trim() == "Collected");
        }, Timeout);
    }

    [Fact]
    public void Register_FiltersAndPagingAreServerSide_AndSearchIsDebouncedIntoOneRequest()
    {
        var register = Register(total: 74, rows: Enumerable.Range(1, 25).Select(i => Row($"Payer {i}", "Prepared", 10m, 0m, 10m)).ToArray());
        Services.AddSingleton(register.Object);
        Services.AddSingleton(EcfApi().Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();
        cut.WaitForAssertion(() => Assert.Contains("1–25 of 74", cut.Markup), Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Next").Click();
        register.Verify(x => x.GetRegisterAsync(It.Is<UtilityRegisterRequest>(r => r.Skip == 25 && r.Take == 25)), Times.Once);

        cut.Find("select[aria-label='Status']").Change("Unpaid");
        register.Verify(x => x.GetRegisterAsync(It.Is<UtilityRegisterRequest>(r => r.Status == "Unpaid" && r.Skip == 0)), Times.Once);

        var search = cut.Find("input[type='search'][aria-label='Search payer, stall or space']");
        search.Input("ab"); search.Input("abc");
        cut.WaitForAssertion(() =>
            register.Verify(x => x.GetRegisterAsync(It.Is<UtilityRegisterRequest>(r => r.Search == "abc")), Times.Once), Timeout);
        register.Verify(x => x.GetRegisterAsync(It.Is<UtilityRegisterRequest>(r => r.Search == "ab")), Times.Never);
    }

    [Fact]
    public void Register_AFailedReadShowsAnErrorWithRetry_NotZeroFigures()
    {
        var register = new Mock<IUtilityRegisterApiClient>();
        register.Setup(x => x.GetRegisterAsync(It.IsAny<UtilityRegisterRequest>()))
            .ReturnsAsync(Result<UtilityRegisterPageDto>.Failure("offline"));
        Services.AddSingleton(register.Object);
        Services.AddSingleton(EcfApi().Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".uw-register [role=alert]");
            Assert.Contains("offline", alert.TextContent);
            Assert.Contains(alert.QuerySelectorAll("button"), b => b.TextContent.Trim() == "Try again");
            Assert.DoesNotContain("₱0.00", cut.Find("dl.v3h-figures").TextContent);
        }, Timeout);
    }

    [Fact]
    public void Register_NoSourcesStatesWhyAndNeverOffersNameMatching()
    {
        Services.AddSingleton(Register().Object);
        Services.AddSingleton(EcfApi().Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();

        cut.WaitForAssertion(() =>
            Assert.Contains("No verified electricity source is registered", cut.Find(".v3-empty").TextContent), Timeout);
    }

    [Fact]
    public void Prepare_IsReviewThenSave_AndSaysNothingIsCollected()
    {
        var row = Row("Maria Santos", "None", null, 0m, 0m, status: "Unprepared", canPrepare: true);
        var register = Register(row);
        register.Setup(x => x.PrepareAsync(It.IsAny<UtilityPrepareRequest>()))
            .ReturnsAsync(Result<UtilityPrepareResult>.Success(new UtilityPrepareResult([new UtilityPrepareOutcome(row.StallId, true, null)])));
        Services.AddSingleton(register.Object);
        Services.AddSingleton(EcfApi().Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();
        cut.WaitForAssertion(() => Assert.Contains("Maria Santos", cut.Markup), Timeout);
        cut.Find(".uw-row-btn").Click();

        cut.Find(".uw-prepare input").Input("125.50");
        Assert.Contains("No collection or SRC is created", cut.Find(".uw-prepare").TextContent);
        cut.FindAll(".uw-prepare button").Single(b => b.TextContent.Trim() == "Review").Click();
        Assert.Contains("Nothing is collected", cut.Find(".uw-review").TextContent);
        register.Verify(x => x.PrepareAsync(It.IsAny<UtilityPrepareRequest>()), Times.Never);       // review alone saves nothing

        cut.FindAll(".uw-prepare button").Single(b => b.TextContent.Trim() == "Save approved amount").Click();
        register.Verify(x => x.PrepareAsync(It.Is<UtilityPrepareRequest>(r =>
            r.Kind == UtilityKind.Electricity && r.Items.Count == 1 && r.Items[0].ApprovedAmount == 125.50m && r.Items[0].StallId == row.StallId)), Times.Once);
    }

    [Fact]
    public void PrepareMultiple_ReviewsBeforeSaving_AndReportsEachSourceOutcome()
    {
        var a = Row("Ana Reyes", "None", null, 0m, 0m, status: "Unprepared", canPrepare: true);
        var b = Row("Ben Cruz", "None", null, 0m, 0m, status: "Unprepared", canPrepare: true);
        var register = Register(a, b);
        register.Setup(x => x.PrepareAsync(It.IsAny<UtilityPrepareRequest>()))
            .ReturnsAsync(Result<UtilityPrepareResult>.Success(new UtilityPrepareResult(
                [new UtilityPrepareOutcome(a.StallId, true, null), new UtilityPrepareOutcome(b.StallId, false, "Already collected directly")])));
        Services.AddSingleton(register.Object);
        Services.AddSingleton(EcfApi().Object);

        var cut = RenderComponent<ElectricityConsumptionFees>();
        cut.WaitForAssertion(() => Assert.Contains("Ana Reyes", cut.Markup), Timeout);
        cut.FindAll("button").Single(x => x.TextContent.Trim() == "Prepare multiple amounts").Click();

        cut.FindAll(".uw-batch-input")[0].Input("100");
        cut.FindAll(".uw-batch-input")[1].Input("200");
        cut.FindAll("[role=dialog] button").Single(x => x.TextContent.Trim() == "Review").Click();
        Assert.Contains("₱300.00", cut.Find("[role=dialog] tfoot").TextContent);
        register.Verify(x => x.PrepareAsync(It.IsAny<UtilityPrepareRequest>()), Times.Never);

        cut.FindAll("[role=dialog] button").Single(x => x.TextContent.Trim() == "Save approved amounts").Click();
        cut.WaitForAssertion(() =>
        {
            var text = cut.Find("[role=dialog]").TextContent;
            Assert.Contains("1 prepared, 1 not changed", text);
            Assert.Contains("Already collected directly", text);
        }, Timeout);
    }

    // ── WCF ──

    [Fact]
    public void Wcf_ShowsItsIdentityAndCashTicket_InsideTheLayoutMain()
    {
        Services.AddSingleton(Register(Row("Maria Santos", "Prepared", 10m, 0m, 10m)).Object);
        Services.AddSingleton(WcfApi().Object);
        Services.AddSingleton(CollectorsApi().Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("main"));
            Assert.Contains("Water Consumption Fees", Assert.Single(cut.FindAll("h1")).TextContent);
            Assert.Contains("Cash Ticket", cut.Find("header").TextContent);
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/operations/water-consumption-fees/report");
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/operations/water-consumption-fees/statements");
        }, Timeout);
    }

    [Fact]
    public void Wcf_IsPreparationAndReview_WithNoWebCollectionEntryOrCustodyForms()
    {
        var api = WcfApi();
        Services.AddSingleton(Register(Row("Maria Santos", "Prepared", 10m, 0m, 10m)).Object);
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFees>();
        cut.WaitForAssertion(() => Assert.Contains("Maria Santos", cut.Markup), Timeout);
        cut.Find(".uw-row-btn").Click();

        Assert.Contains("recorded by Collectors on Mobile", cut.Find(".uw-detail").TextContent);
        Assert.Empty(cut.FindAll("form"));
        Assert.DoesNotContain("Receive book", cut.Markup);
        Assert.DoesNotContain("Post collection", cut.Markup);
        Assert.DoesNotContain("Record payment", cut.Markup);
        Assert.DoesNotContain("unassigned CT", cut.Markup);        // physical stock never gates readiness (IA-062)
        Assert.DoesNotContain(cut.FindAll("h2"), h => h.TextContent.Trim() == "Collections needing review");     // an empty review list is not shown
        api.Verify(x => x.PostAsync(It.IsAny<WcfCollectionPostRequest>()), Times.Never);
        api.Verify(x => x.GetBooksAsync(), Times.Never);
    }

    [Fact]
    public void Wcf_PresentsTheDirectApprovedCashTicketOperation_WithNoMeterWording()
    {
        Services.AddSingleton(Register(Row("Maria Santos", "Prepared", 10m, 0m, 10m)).Object);
        Services.AddSingleton(WcfApi().Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Cash Ticket", cut.Markup);
            foreach (var meter in new[] { "meter", "cubic", "m³", "consumption ×" })
                Assert.DoesNotContain(meter, cut.Find("table").TextContent, StringComparison.OrdinalIgnoreCase);
        }, Timeout);
    }

    [Fact]
    public void Wcf_LegacySource_OffersAuthorityReview_AndCanonicalSourceDoesNot()
    {
        Services.AddSingleton(Register(
            Row("Legacy Payer", "Prepared", 10m, 0m, 10m, authority: SettlementAuthority.Legacy),
            Row("Canonical Payer", "Prepared", 10m, 0m, 10m, authority: SettlementAuthority.Canonical)).Object);
        Services.AddSingleton(WcfApi().Object);
        Services.AddSingleton(CollectorsApi().Object);

        var cut = RenderComponent<WaterConsumptionFees>();
        cut.WaitForAssertion(() => Assert.Contains("Legacy Payer", cut.Markup), Timeout);

        cut.FindAll(".uw-row-btn").Single(b => b.TextContent.Contains("Legacy Payer")).Click();
        Assert.Contains(cut.FindAll(".uw-detail button"), b => b.TextContent.Trim() == "Review authority");
        cut.FindAll(".uw-row-btn").Single(b => b.TextContent.Contains("Canonical Payer")).Click();
        Assert.DoesNotContain(cut.FindAll(".uw-detail button"), b => b.TextContent.Trim() == "Review authority");
    }

    // ── WCF Mobile collection: one operation-level enable, readiness derived by the server ──

    private static WcfMobileStatusDto Status(bool active = false, params WcfReadinessItemDto[] blockers) => new(
        active, active ? new DateOnly(2026, 10, 1) : null, active ? DateTime.UtcNow : null, active ? "head" : null,
        !active && blockers.Length == 0, blockers,
        [new WcfReadinessItemDto("POLICY", "Water policy", "Cash Ticket · direct amount", true, null, null)],
        [new WcfCollectorReadinessDto(Guid.NewGuid(), "Bobby Mercado", true, blockers.Length == 0 ? 4999 : 0, 0, blockers.Length == 0)],
        0);

    [Fact]
    public void WcfMobile_ReadyToEnable_OffersOneAction_AndAsksTheOfficeToTypeNothing()
    {
        var api = WcfApi();
        api.Setup(x => x.GetMobileStatusAsync()).ReturnsAsync(Result<WcfMobileStatusDto>.Success(Status()));
        api.Setup(x => x.EnableMobileAsync()).ReturnsAsync(Result<WcfMobileStatusDto>.Success(Status(active: true)));
        Services.AddSingleton(Register().Object);
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() => Assert.Contains("Ready to enable", cut.Find("section.wcf-mobile").TextContent), Timeout);
        var panel = cut.Find("section.wcf-mobile");
        Assert.Empty(panel.QuerySelectorAll("input"));
        Assert.DoesNotContain("Check readiness", cut.Markup);
        Assert.DoesNotContain("Evidence reference", cut.Markup);

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Enable Mobile Collection").Click();
        cut.WaitForAssertion(() => Assert.Contains("Historical Water records", cut.Find("[role='dialog']").TextContent), Timeout);
        Assert.Empty(cut.Find("[role='dialog']").QuerySelectorAll("input"));
        cut.Find("[role='dialog'] .v3-btn-primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("section.wcf-mobile"));                                                 // active: nothing to say about it
            Assert.Empty(cut.FindAll("[role='dialog']"));
        }, Timeout);
        api.Verify(x => x.EnableMobileAsync(), Times.Once);
    }

    [Fact]
    public void WcfMobile_Blocked_ShowsOnlyTheRealBlocker_WithItsAction()
    {
        var api = WcfApi();
        api.Setup(x => x.GetMobileStatusAsync()).ReturnsAsync(Result<WcfMobileStatusDto>.Success(Status(false,
            new WcfReadinessItemDto("CASH_TICKETS", "Cash Ticket custody", "Cash Tickets have not been assigned to Bobby Mercado.",
                false, "Manage Accountable Forms", "/accountable-forms"))));
        Services.AddSingleton(Register().Object);
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Needs attention", cut.Find("section.wcf-mobile").TextContent);
            var blocker = Assert.Single(cut.FindAll(".wcf-blockers li"));
            Assert.Contains("Bobby Mercado", blocker.TextContent);
            Assert.Equal("/accountable-forms", blocker.QuerySelector("a")!.GetAttribute("href"));
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Enable Mobile Collection");
        }, Timeout);
    }

    [Fact]
    public void WcfMobile_Active_StatesItPlainly_WithNoEnableAction()
    {
        var api = WcfApi();
        api.Setup(x => x.GetMobileStatusAsync()).ReturnsAsync(Result<WcfMobileStatusDto>.Success(Status(active: true)));
        Services.AddSingleton(Register(Row("Maria Santos", "Prepared", 10m, 0m, 10m)).Object);
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Maria Santos", cut.Markup);
            Assert.Empty(cut.FindAll("section.wcf-mobile"));
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Enable Mobile Collection");
            foreach (var technical in new[] { "Settlement authority", "Pending cutover", "cutover" })
                Assert.DoesNotContain(technical, cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(cut.Find("dl.v3h-figures"));                                                      // the same hero family as ECF
        }, Timeout);
    }

    [Fact]
    public void WcfActivity_ShowsReadableFacts_NeverTheStoredJsonEvidence_AndFiltersBySearch()
    {
        var api = WcfApi();
        const string evidence = "{\"schemaVersion\":1,\"municipalityId\":\"6b8e1a52-0000-0000-0000-000000000001\",\"utilityBillId\":\"6b8e1a52-0000-0000-0000-000000000002\",\"stallNo\":\"F-12\",\"billingYear\":2026,\"billingMonth\":9,\"ratePerCubicMeter\":0}";
        api.Setup(x => x.GetActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfCollectionActivityDto>>.Success(
            [
                new WcfCollectionActivityDto(Guid.NewGuid(), new DateOnly(2026, 10, 1), DateTime.UtcNow, "CT000101", "Maria Santos",
                    120m, 1, "Posted",
                    [new WcfCollectionActivityLineDto("WCF", 120m, 2026, 9, "UtilityBill / Water", evidence, [evidence])]),
                new WcfCollectionActivityDto(Guid.NewGuid(), new DateOnly(2026, 10, 2), DateTime.UtcNow, "CT000102", "Other Payer",
                    50m, 1, "Posted", [])
            ]));
        Services.AddSingleton(Register().Object);
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<WaterConsumptionFees>();

        cut.WaitForAssertion(() =>
        {
            var facts = cut.Find("dl.wcf-facts").TextContent;
            Assert.Contains("F-12", facts);
            Assert.Contains("Water (WCF)", facts);
            Assert.Contains("CT000101", facts);
            foreach (var technical in new[] { "schemaVersion", "municipalityId", "utilityBillId", "ratePerCubicMeter", "{", "6b8e1a52" })
                Assert.DoesNotContain(technical, cut.Markup);
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/collections/activity?src=CT000101");
        }, Timeout);

        cut.Find("input[aria-label='Search WCF collections']").Input("Other");
        Assert.DoesNotContain("CT000101", cut.Find("section[aria-labelledby='wcf-activity-title'] tbody").TextContent);
        Assert.Contains("CT000102", cut.Find("section[aria-labelledby='wcf-activity-title'] tbody").TextContent);
    }

    // ── Doubles ──

    private static UtilityRegisterRowDto Row(string payer, string mode, decimal? approved, decimal collected, decimal outstanding,
        string? status = null, bool canPrepare = false, SettlementAuthority authority = SettlementAuthority.Canonical) => new(
        Guid.NewGuid(), mode == "None" ? null : Guid.NewGuid(), "12", FacilityCode.NPM, "Tenant Market", "DryGoods", payer,
        approved, collected, outstanding, mode,
        status ?? (mode switch { "Direct" => "Direct", "None" => "Unprepared", _ => outstanding <= 0m ? "Settled" : collected > 0m ? "Partial" : "Unpaid" }),
        authority, 1, canPrepare, null);

    private static Mock<IUtilityRegisterApiClient> Register(params UtilityRegisterRowDto[] rows) => Register(rows.Length, rows);

    private static Mock<IUtilityRegisterApiClient> Register(int total, UtilityRegisterRowDto[] rows)
    {
        var api = new Mock<IUtilityRegisterApiClient>();
        api.Setup(x => x.GetRegisterAsync(It.IsAny<UtilityRegisterRequest>()))
            .ReturnsAsync(() => Result<UtilityRegisterPageDto>.Success(new UtilityRegisterPageDto(
                rows, total,
                new UtilityRegisterTotalsDto(rows.Sum(r => r.Approved ?? 0m), rows.Sum(r => r.Collected), rows.Sum(r => r.Outstanding), 0m, total),
                [new UtilityFacilityOptionDto(FacilityCode.NPM, "Tenant Market")], ["DryGoods"])));
        return api;
    }

    private static Mock<IEcfCollectionsApiClient> EcfApi(EcfCollectionDraftDto? draft = null)
    {
        var api = new Mock<IEcfCollectionsApiClient>();
        api.Setup(x => x.GetCurrentDraftAsync()).ReturnsAsync(draft is null ? Result<EcfCollectionDraftDto>.NotFound() : Result<EcfCollectionDraftDto>.Success(draft));
        api.Setup(x => x.GetActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<EcfCollectionActivityDto>>.Success(Array.Empty<EcfCollectionActivityDto>()));
        return api;
    }

    private static EcfCollectionDraftDto Draft() => new(
        Guid.NewGuid(), 1, new DateOnly(2026, 10, 1), "Open", false, null, "Juan Dela Cruz", null, null, null, 40m, null, []);

    private static Mock<IWcfCollectionsApiClient> WcfApi()
    {
        var api = new Mock<IWcfCollectionsApiClient>();
        api.Setup(x => x.GetActivityAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfCollectionActivityDto>>.Success(Array.Empty<WcfCollectionActivityDto>()));
        api.Setup(x => x.GetReconciliationExceptionsAsync())
            .ReturnsAsync(Result<IReadOnlyList<WcfReconciliationExceptionDto>>.Success(Array.Empty<WcfReconciliationExceptionDto>()));
        api.Setup(x => x.GetBooksAsync())
            .ReturnsAsync(Result<IReadOnlyList<AccountableFormBookDto>>.Success(Array.Empty<AccountableFormBookDto>()));
        api.Setup(x => x.GetSetupSourcesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<WcfSetupSourceDto>>.Success(Array.Empty<WcfSetupSourceDto>()));
        return api;
    }

    private static Mock<ICollectorsApiClient> CollectorsApi()
    {
        var api = new Mock<ICollectorsApiClient>();
        api.Setup(x => x.GetAllCollectorsAsync())
            .ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(Array.Empty<CollectorListDto>()));
        return api;
    }
}
