using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>Both the Head and Admins read the office's billing rules; only the Head is offered the form, and a penalty rate is never assumed.</summary>
public sealed class BillingPolicySettingsTests : TestContext
{
    private static readonly DateOnly Today = new(2026, 11, 5);
    private readonly Mock<IBillingPolicyApiClient> _api = new();
    private readonly Mock<ISettingsApiClient> _settings = new();

    private static BillingPolicyPageDto Defaults() => new(BillingPolicy.StallTrackDefaults, null, [], Today);

    [Fact]
    public void HowAccountsAreClassified_IsStatedHere_AndAFailedReadSaysSo_WithoutCostingTheRules()
    {
        _settings.Setup(s => s.GetSystemSettingsAsync()).ReturnsAsync(Result<EEMOCantilanSDS.Application.Dtos.Settings.SystemSettingsDto>.Success(new(
            new("Office", "Town", "Province", "StallTrack", "Office"), new(15, 7, 5, 15, []), new(2, 12, 3, "UTC+8"), new("StallTrack", "1", "Test", "UTC+8", DateTime.UtcNow), [])));
        var cut = Open("Admin", Result<BillingPolicyPageDto>.Success(Defaults()));

        cut.WaitForAssertion(() => Assert.Contains("2+ overdue months this year", cut.Markup));
        Assert.Contains("Earlier years, expired or ended contracts, per obligation", cut.Markup);
        Assert.DoesNotContain("Rolling", cut.Markup);

        _settings.Setup(s => s.GetSystemSettingsAsync()).ReturnsAsync(Result<EEMOCantilanSDS.Application.Dtos.Settings.SystemSettingsDto>.Failure("Unavailable", 500));
        var failed = RenderComponent<BillingPolicySettings>();
        failed.WaitForAssertion(() => Assert.Contains("The Delinquent and Arrears definitions could not be loaded.", failed.Markup));
        Assert.Contains("Oldest month first", failed.Markup);
    }

    private IRenderedComponent<BillingPolicySettings> Open(string role, Result<BillingPolicyPageDto> page)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Staff").SetRoles(role);
        _api.Setup(a => a.GetAsync()).ReturnsAsync(page);
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(_settings.Object);
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        var cut = RenderComponent<BillingPolicySettings>();
        // The amendment form opens from Edit; the tests below work in the open form.
        if (role == "SuperAdmin" && page.IsSuccess)
        {
            cut.WaitForState(() => cut.FindAll(".bp-edit").Count > 0);
            cut.Find(".bp-edit").Click();
        }
        return cut;
    }

    [Fact]
    public void ThePolicyReadsAsAStatement_UntilEditOpensTheForm_AndCancelClosesIt()
    {
        var cut = Open("SuperAdmin", Result<BillingPolicyPageDto>.Success(Defaults()));
        Assert.NotEmpty(cut.FindAll("form.bp-form"));
        Assert.Empty(cut.FindAll(".bp-edit"));

        cut.FindAll(".bp-form-foot button").Single(b => b.TextContent.Trim() == "Cancel").Click();

        Assert.Empty(cut.FindAll("form.bp-form"));
        Assert.Single(cut.FindAll(".bp-edit"));
    }

    [Fact]
    public void WithNothingSet_TheDefaultsAreStated_AndNoPenaltyIsClaimed()
    {
        var cut = Open("SuperAdmin", Result<BillingPolicyPageDto>.Success(Defaults()));

        cut.WaitForAssertion(() => Assert.Contains("StallTrack defaults", cut.Markup));
        Assert.Contains("Last day of the month", cut.Markup);
        Assert.Contains("Oldest month first", cut.Markup);
        Assert.Contains("Up to 3 months", cut.Markup);
        Assert.Contains("Not configured", cut.Markup);
        Assert.Contains("Nothing is charged", cut.Find(".bp-terms").TextContent);
        Assert.DoesNotContain("Per ", cut.Find(".bp-terms").TextContent);
    }

    [Fact]
    public void TheMonthRentIsCountedFrom_IsStated_AndTheHeadCanMoveIt()
    {
        RecordBillingPolicyRequest? sent = null;
        _api.Setup(a => a.RecordAsync(It.IsAny<RecordBillingPolicyRequest>())).Callback((RecordBillingPolicyRequest r) => sent = r)
            .ReturnsAsync(Result<BillingPolicyPageDto>.Failure("stop here"));
        var cut = Open("SuperAdmin", Result<BillingPolicyPageDto>.Success(Defaults()));
        cut.WaitForAssertion(() => Assert.Contains("December 2026", cut.Markup));

        cut.FindAll("label.v3-field").Single(l => l.TextContent.StartsWith("Count unpaid months from")).QuerySelector("input")!.Change("2024-03");
        cut.FindAll("label.v3-field").Single(l => l.TextContent.StartsWith("Due day")).QuerySelector("select")!.Change("10");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal(new DateOnly(2024, 3, 1), sent?.Rules.CountsMonthsFrom));
        Assert.Equal(10, sent!.Rules.DueDay);
    }

    [Fact]
    public void AnAdminReadsTheRules_ButIsNotOfferedTheForm()
    {
        var cut = Open("Admin", Result<BillingPolicyPageDto>.Success(Defaults()));

        cut.WaitForAssertion(() => Assert.Contains("Current policy", cut.Markup));
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void TheHeadSettingLateCharges_SendsTheSurchargeAndInterestWithTheirAuthority_AndSeesTheServersAnswer()
    {
        RecordBillingPolicyRequest? sent = null;
        var rules = BillingPolicy.StallTrackDefaults with { PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 25m, InterestPercent = 2m, InterestMaxMonths = 36 };
        var saved = new BillingPolicyPageDto(rules, new(Guid.NewGuid(), Today, rules, "RA 7160 Sec. 168", DateTime.UtcNow),
            [new(Guid.NewGuid(), Today, rules, "RA 7160 Sec. 168", DateTime.UtcNow)], Today);
        _api.Setup(a => a.RecordAsync(It.IsAny<RecordBillingPolicyRequest>())).Callback((RecordBillingPolicyRequest r) => sent = r)
            .ReturnsAsync(Result<BillingPolicyPageDto>.Success(saved));
        var cut = Open("SuperAdmin", Result<BillingPolicyPageDto>.Success(Defaults()));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form")));

        Field(cut, "Surcharge").Change("25");
        Field(cut, "Interest").Change("2");
        Assert.Equal("36", Field(cut, "Interest stops after").GetAttribute("value"));   // Sec. 168's limit unless the Head changes it
        Field(cut, "Authority").Change("RA 7160 Sec. 168");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("The billing policy was recorded.", cut.Markup));
        Assert.Equal(new RecordBillingPolicyRequest(Today, rules, "RA 7160 Sec. 168"), sent);
        Assert.Contains("25% surcharge once + 2% a month", cut.Markup);
        Assert.Contains(cut.FindAll(".bp-term-note"), n => n.TextContent == "Interest up to 36 months · RA 7160 Sec. 168");
    }

    private static AngleSharp.Dom.IElement Field(IRenderedComponent<BillingPolicySettings> cut, string label) =>
        cut.FindAll("label.v3-field").Single(l => l.QuerySelector("span")!.TextContent == label).QuerySelector("input")!;

    [Fact]
    public void ARefusal_IsStated_InTheServersWords()
    {
        _api.Setup(a => a.RecordAsync(It.IsAny<RecordBillingPolicyRequest>())).ReturnsAsync(Result<BillingPolicyPageDto>.Failure("State the ordinance or authority behind the penalty rate."));
        var cut = Open("SuperAdmin", Result<BillingPolicyPageDto>.Success(Defaults()));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form")));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("State the ordinance", cut.Find("[role=alert]").TextContent));
    }

    [Fact]
    public void AFailedRead_SaysSo_AndCanBeRetried()
    {
        var cut = Open("Admin", Result<BillingPolicyPageDto>.Failure("Unavailable"));
        cut.WaitForAssertion(() => Assert.Contains("couldn't be loaded", cut.Markup));
        Assert.DoesNotContain("StallTrack defaults", cut.Markup);

        _api.Setup(a => a.GetAsync()).ReturnsAsync(Result<BillingPolicyPageDto>.Success(Defaults()));
        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => Assert.Contains("StallTrack defaults", cut.Markup));
    }
}
