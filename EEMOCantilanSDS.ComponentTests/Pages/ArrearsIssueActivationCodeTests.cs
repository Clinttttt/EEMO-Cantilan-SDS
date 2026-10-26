using Bunit;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// ST-19 Head Portal: "Issue activation code" on the Arrears register. The action appears only on a row that binds to a liable id
/// (the original contract, or the space obligation account) and still owes money; the server alone decides everything else.
/// </summary>
public sealed class ArrearsIssueActivationCodeTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid Contract = Guid.NewGuid();
    private static readonly Guid SpaceAccount = Guid.NewGuid();
    private readonly Mock<IReportsApiClient> _reports = new();
    private readonly Mock<IActivationCodesApiClient> _codes = new();

    public ArrearsIssueActivationCodeTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton(Mock.Of<IObligationsApiClient>());
        Services.AddSingleton(Mock.Of<IGovernedServicesApiClient>());
        Services.AddSingleton(Mock.Of<IOfficialReportsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        Services.AddSingleton(_reports.Object);
        Services.AddSingleton(_codes.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        var scope = new FollowUpScopeDto(FollowUpScopeKind.MonthlySpace, "KANMANGGAY_SPACE_RENTAL", "Kanmanggay");
        var queue = Result<FollowUpQueueDto>.Success(new FollowUpQueueDto("October 2026", new DateOnly(2026, 10, 26),
            [
                new(1, "High", "Delinquent", "delinquent", FacilityCode.TCC, "Monthly rental", "Pedro Reyes", "Stall 4", 8_000m, false,
                    "Jul 2026 – Sep 2026", "Unpaid · 3 months", "Record payment", "/profile/tcc/4", StallId: Guid.NewGuid(), ContractId: Contract),
                new(1, "Critical", "Delinquent", "delinquent", FacilityCode.NCC, "Monthly rental", "Rosa Magbanua", "Stall 3", 2_700m, false,
                    "3 months to September 2026", "Unpaid · 3 months", "Record payment", "/profile/ncc/3", StallId: Guid.NewGuid()),
            ],
            [
                new ObligationFollowUpItemDto(SpaceAccount, Guid.NewGuid(), "Lisa Ilogans", "K-3", scope, new DateOnly(2026, 6, 1), 1_000m, 0m, 1_000m, "High", null, "/kanmanggay"),
                new ObligationFollowUpItemDto(Guid.NewGuid(), Guid.NewGuid(), null, "K-9", scope, new DateOnly(2026, 6, 1), 1_000m, 0m, 1_000m, "High", null, "/kanmanggay"),
            ]));
        _reports.Setup(x => x.GetFinancialReportAsync(ReportPeriod.Monthly, It.IsAny<int>(), It.IsAny<int?>(), null, false))
            .ReturnsAsync(Result<FinancialReportDto>.Success(ReportPageTests.SampleReport()));
        _reports.Setup(x => x.GetFollowUpHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(queue);
    }

    private IRenderedComponent<Arrears> RenderPage()
    {
        var cut = RenderComponent<Arrears>();
        cut.WaitForAssertion(() => Assert.Contains("Rosa Magbanua", cut.Markup), Timeout);
        return cut;
    }

    private static IReadOnlyList<AngleSharp.Dom.IElement> Issue(IRenderedComponent<Arrears> cut) =>
        cut.FindAll("button").Where(b => b.TextContent.Trim() == "Issue activation code").ToList();

    private void Open(IRenderedComponent<Arrears> cut, string payer)
    {
        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[role=dialog]").Count == 0)
                cut.FindAll("button").First(b => b.GetAttribute("aria-label")?.Contains(payer) == true).Click();
            Assert.NotNull(cut.Find("[role=dialog]"));
        }, Timeout);
    }

    // The page re-renders while its lists finish loading, so a handler found a moment ago can be gone: retry the ACTION (never an assertion) until it lands.
    private static void Do(IRenderedComponent<Arrears> cut, Action action) => cut.WaitForAssertion(action, Timeout);

    private void Fill(IRenderedComponent<Arrears> cut, string phone = "09171234567", string? days = null)
    {
        Do(cut, () => cut.Find("#hcd-phone").Input(phone));
        if (days is not null) cut.Find("#hcd-days").Input(days);
        Do(cut, () => cut.Find(".hcd-confirm input").Change(true));
    }

    private static HistoricalAccessCodeDto Issued(string mode) =>
        new(ArrearsAccountKind.StallRent, Contract, "Taboan Public Market", "Stall 4", "Pedro Reyes", "ABCD-1234", "09171234567", mode, new DateTime(2026, 11, 25, 4, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void OnlyRowsBoundToALiableIdThatStillOweMoney_OfferTheAction_AndPageLoadCallsNothingNew()
    {
        var cut = RenderPage();

        Assert.Equal(2, Issue(cut).Count);   // the ended-contract stall row and the named space account only
        Assert.Contains(cut.FindAll("button"), b => b.GetAttribute("aria-label")?.Contains("Pedro Reyes") == true);
        Assert.Contains(cut.FindAll("button"), b => b.GetAttribute("aria-label")?.Contains("Lisa Ilogans") == true);
        Assert.DoesNotContain(cut.FindAll("button"), b => b.GetAttribute("aria-label")?.Contains("Rosa Magbanua") == true);   // no original-contract id on a live row
        Assert.Empty(cut.FindAll("[role=dialog]"));
        _codes.VerifyNoOtherCalls();
        _reports.Verify(x => x.GetFollowUpHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public void IssuingForAStallAccount_SendsTheOriginalContract_AndShowsTheCodeOnce()
    {
        HistoricalAccessCodeDto? shown = null;
        IssueHistoricalAccessRequest? sent = null;
        _codes.Setup(x => x.IssueHistoricalAsync(It.IsAny<IssueHistoricalAccessRequest>()))
            .Callback<IssueHistoricalAccessRequest>(r => sent = r)
            .ReturnsAsync(() => Result<HistoricalAccessCodeDto>.Success(shown = Issued("Activation")));
        var cut = RenderPage();
        Open(cut, "Pedro Reyes");
        Assert.Contains("disabled", cut.Find("button[type=submit]").OuterHtml);   // payer confirmation required first

        Fill(cut, days: "45");
        Do(cut, () => cut.Find("form").Submit());

        cut.WaitForAssertion(() => Assert.Contains("ABCD-1234", cut.Markup), Timeout);
        Assert.Equal(new IssueHistoricalAccessRequest(ArrearsAccountKind.StallRent, Contract, "09171234567", "Pedro Reyes", 45), sent);
        var text = cut.Find("[role=dialog]").TextContent;
        Assert.Contains("Activation code", text);
        Assert.Contains("Single-use", text);
        Assert.Contains("Replaces any earlier unused code", text);
        Assert.Contains("only time the code is shown", text);
        Assert.Contains("Nov 25, 2026", text);

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Done").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role=dialog]")), Timeout);
        Assert.DoesNotContain("ABCD-1234", cut.Markup);
        Open(cut, "Pedro Reyes");
        Assert.DoesNotContain("ABCD-1234", cut.Markup);   // never shown again
    }

    [Fact]
    public void IssuingForASpaceAccount_UsesTheObligationAccount_AndALinkCodeExplainsNoSecondAccount()
    {
        IssueHistoricalAccessRequest? sent = null;
        _codes.Setup(x => x.IssueHistoricalAsync(It.IsAny<IssueHistoricalAccessRequest>()))
            .Callback<IssueHistoricalAccessRequest>(r => sent = r)
            .ReturnsAsync(Result<HistoricalAccessCodeDto>.Success(Issued("Link") with { Kind = ArrearsAccountKind.SpaceRental, LiableId = SpaceAccount }));
        var cut = RenderPage();
        Open(cut, "Lisa Ilogans");
        Fill(cut);
        Do(cut, () => cut.Find("form").Submit());

        cut.WaitForAssertion(() => Assert.Contains("Link code", cut.Markup), Timeout);
        Assert.Equal(new IssueHistoricalAccessRequest(ArrearsAccountKind.SpaceRental, SpaceAccount, "09171234567", "Lisa Ilogans", 30), sent);
        Assert.Contains("no second account is created", cut.Find("[role=dialog]").TextContent);
    }

    [Theory]
    [InlineData(ResultStatus.Forbidden, null, "not allowed to issue")]
    [InlineData(ResultStatus.NotFound, null, "no longer an unpaid account")]
    [InlineData(ResultStatus.Invalid, "Validity must be between 1 and 90 days.", "Validity must be between")]
    public void ServerRefusals_AreExplicitStates_AndNoCodeIsShown(ResultStatus status, string? error, string expected)
    {
        _codes.Setup(x => x.IssueHistoricalAsync(It.IsAny<IssueHistoricalAccessRequest>()))
            .ReturnsAsync(error is null ? status == ResultStatus.Forbidden ? Result<HistoricalAccessCodeDto>.Forbidden() : Result<HistoricalAccessCodeDto>.NotFound()
                : Result<HistoricalAccessCodeDto>.Failure(error, status));
        var cut = RenderPage();
        Open(cut, "Pedro Reyes");
        Fill(cut);
        Do(cut, () => cut.Find("form").Submit());

        cut.WaitForAssertion(() => Assert.Contains(expected, cut.Find("[role=alert]").TextContent), Timeout);
        Assert.Empty(cut.FindAll(".hcd-code"));
    }

    [Fact]
    public void AStaleRow_ShowsTheConflictMessage_AndRefreshReloadsTheRegister()
    {
        _codes.Setup(x => x.IssueHistoricalAsync(It.IsAny<IssueHistoricalAccessRequest>()))
            .ReturnsAsync(Result<HistoricalAccessCodeDto>.Failure("The liable payer on this account has changed. Refresh the Arrears list and confirm the payer again.", ResultStatus.Conflict));
        var cut = RenderPage();
        Open(cut, "Pedro Reyes");
        Fill(cut);
        Do(cut, () => cut.Find("form").Submit());

        cut.WaitForAssertion(() => Assert.Contains("liable payer on this account has changed", cut.Find("[role=alert]").TextContent), Timeout);
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Refresh the Arrears list").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role=dialog]")), Timeout);
        _reports.Verify(x => x.GetFollowUpHistoryAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Exactly(2));
    }

    [Fact]
    public void AnUnreachableServer_IsStated_AndBadInputNeverCallsTheApi()
    {
        _codes.Setup(x => x.IssueHistoricalAsync(It.IsAny<IssueHistoricalAccessRequest>())).ThrowsAsync(new HttpRequestException());
        var cut = RenderPage();
        Open(cut, "Pedro Reyes");

        Fill(cut, phone: "  ", days: "30");
        Do(cut, () => cut.Find("form").Submit());
        Assert.Contains("mobile number", cut.Find("[role=alert]").TextContent);
        Do(cut, () => cut.Find("#hcd-phone").Input("09171234567"));
        Do(cut, () => cut.Find("#hcd-days").Input("91"));
        Do(cut, () => cut.Find("form").Submit());
        Assert.Contains("between 1 and 90", cut.Find("[role=alert]").TextContent);
        _codes.VerifyNoOtherCalls();

        Do(cut, () => cut.Find("#hcd-days").Input("30"));
        Do(cut, () => cut.Find("form").Submit());
        cut.WaitForAssertion(() => Assert.Contains("reach the server", cut.Find("[role=alert]").TextContent), Timeout);
    }

    [Fact]
    public void TheDialog_IsLabelled_TrapsFocusThroughTheSharedBinder_AndEscapeCloses()
    {
        var cut = RenderPage();
        Open(cut, "Pedro Reyes");

        var dialog = cut.Find("[role=dialog]");
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal("Issue activation code", cut.Find("#" + dialog.GetAttribute("aria-labelledby")).TextContent);
        foreach (var id in new[] { "hcd-phone", "hcd-days" })
            Assert.Equal("LABEL", cut.Find($"#{id}").ParentElement!.TagName);   // wrapped in a visible label
        Assert.Equal("Close", cut.Find(".hcd-close").GetAttribute("aria-label"));
        JSInterop.VerifyInvoke("import");

        dialog.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role=dialog]")), Timeout);
        _codes.VerifyNoOtherCalls();
    }
}
