using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Revenue.CreateRevenueClassification;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Securities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.HttpClients.ApiClients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class RevenueSetupTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Page_IsRestrictedToSuperAdmin()
    {
        var authorize = Assert.Single(typeof(RevenueSetup).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        var route = Assert.Single(typeof(RevenueSetup).GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>());

        Assert.Equal("SuperAdmin", authorize.Roles);
        Assert.Equal("/settings/revenue", route.Template);
    }

    [Fact]
    public void Directory_GroupsSourcesByTheOfficialIncomeClassification_WithArrearsAndFinesUnderOthers()
    {
        var (cut, _) = RenderPage([
            Classification("MARKET_FEES", "Market Fees", true, true, Policy("MARKET_FEES", "Market Fees", PhilippineTime.Today, RevenueInstrumentType.CashTicket)),
            Classification("PERMANENT_STALL_RENT", "Permanent Stall Rent", true, true, Policy("PERMANENT_STALL_RENT", "Permanent Stall Rent", PhilippineTime.Today, RevenueInstrumentType.OfficialReceipt)),
            Classification("VEGETABLE_FRUIT_SPACE_RENTAL", "Vegetable/Fruit Space Rental", true, true, Policy("VEGETABLE_FRUIT_SPACE_RENTAL", "Vegetable/Fruit Space Rental", PhilippineTime.Today, RevenueInstrumentType.OfficialReceipt)),
            Classification("TERMINAL_TRICYCAD", "Tricycad", true, true, Policy("TERMINAL_TRICYCAD", "Tricycad", PhilippineTime.Today, RevenueInstrumentType.CashTicket)),
            Classification("SLAUGHTERHOUSE", "Slaughterhouse", true, true, Policy("SLAUGHTERHOUSE", "Slaughterhouse", PhilippineTime.Today, RevenueInstrumentType.OfficialReceipt)),
            Classification("ARREARS", "Arrears", true, true, Policy("ARREARS", "Arrears", PhilippineTime.Today, null)),
            Classification("PENALTIES_AND_FINES", "Penalties/Fines", true, true, Policy("PENALTIES_AND_FINES", "Penalties/Fines", PhilippineTime.Today, RevenueInstrumentType.OfficialReceipt)),
            Classification("LOCAL_EXTRA", "Office's own source", false, true, Policy("LOCAL_EXTRA", "Office's own source", PhilippineTime.Today, null)),
        ]);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Income from Market", "Rent Income — Stall Rental", "Space Rental", "Income from Terminal", "Income from Slaughterhouse", "Others"],
                cut.FindAll(".rev-group-head h2").Select(h => h.TextContent.Trim()).ToArray());
            var others = cut.FindAll(".rev-group").Last().TextContent;
            Assert.Contains("Arrears", others);
            Assert.Contains("Penalties/Fines", others);
            Assert.Contains("Office's own source", others);   // a source off the official list is listed, never hidden
            Assert.Contains("Retired", others);
        }, RenderTimeout);

        // Vegetable / Fruits keeps both instruments; Arrears is given none.
        var vegetable = cut.FindAll(".rev-row").Single(r => r.TextContent.Contains("Vegetable/Fruit"));
        Assert.Equal(["OR", "CT"], vegetable.QuerySelectorAll(".rev-row-instrument [aria-hidden='true']").Select(e => e.TextContent.Trim()).Where(t => t is "OR" or "CT").ToArray());
        var arrears = cut.FindAll(".rev-row").Single(r => r.TextContent.Contains("Arrears"));
        Assert.DoesNotContain("OR", arrears.QuerySelector(".rev-row-instrument")!.TextContent);
        Assert.DoesNotContain("CT", arrears.QuerySelector(".rev-row-instrument")!.TextContent);
    }

    [Fact]
    public void Directory_OffersNoAddChangeRetireOrActivationControls()
    {
        var (cut, api) = RenderPage([Classification("MARKET_FEES", "Market Fees", true, true, Policy("MARKET_FEES", "Market Fees", PhilippineTime.Today, RevenueInstrumentType.CashTicket))]);

        cut.WaitForAssertion(() => Assert.Contains("Market Fees", cut.Markup), RenderTimeout);
        Assert.Equal("Revenue Sources", cut.Find("h1").TextContent.Trim());
        var labels = cut.FindAll("button").Select(b => b.TextContent.Trim()).ToArray();
        Assert.DoesNotContain(labels, l => l.Contains("Add revenue source") || l.Contains("Schedule policy change") || l.Contains("Retire")
            || l.Contains("Activate") || l.Contains("Deactivate"));
        Assert.Empty(cut.FindAll("input[type=date]"));
        api.Verify(client => client.CreateClassificationAsync(It.IsAny<CreateRevenueClassificationCommand>()), Times.Never);
    }

    [Fact]
    public void PolicyHistory_PreservesNewestFirstAndLabelsScheduledCurrentAndPrevious()
    {
        var id = Guid.NewGuid();
        var classification = Classification("STALL_RENT", "Stall Rent", true, true,
            Policy("STALL_RENT", "Stall Rent", PhilippineTime.Today, RevenueInstrumentType.OfficialReceipt, id), id);
        var future = Policy("STALL_RENT", "Future Stall Rent", PhilippineTime.Today.AddDays(5), RevenueInstrumentType.CashTicket, Guid.NewGuid());
        var current = Policy("STALL_RENT", "Stall Rent", PhilippineTime.Today, RevenueInstrumentType.OfficialReceipt, id);
        var previous = Policy("STALL_RENT", "Former Stall Rent", PhilippineTime.Today.AddDays(-30), RevenueInstrumentType.OfficialReceipt, Guid.NewGuid());
        var (cut, api) = RenderPage([classification], id, [future, current, previous]);

        cut.WaitForAssertion(() => Assert.Contains("Stall Rent", cut.Markup), RenderTimeout);
        ClickButton(cut, "Details");

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".rev-history-table tbody tr");
            Assert.Equal(3, rows.Count);
            Assert.Contains("Future Stall Rent", rows[0].TextContent);
            Assert.Contains("Scheduled", rows[0].TextContent);
            Assert.Contains("In effect", rows[1].TextContent);
            Assert.Contains("Previous", rows[2].TextContent);
        }, RenderTimeout);
        api.Verify(client => client.GetPolicyHistoryAsync(id), Times.Once);
    }

    [Fact]
    public void Details_OpenInADrawerBesideTheDirectory_WithTheInternalReference_AndEscapeClosesIt()
    {
        var id = Guid.NewGuid();
        var current = Policy("STALL_RENT", "Stall Rent", PhilippineTime.Today, RevenueInstrumentType.OfficialReceipt, id);
        var (cut, _) = RenderPage([Classification("STALL_RENT", "Stall Rent", true, true, current, id)], id, [current]);

        cut.WaitForAssertion(() => Assert.Contains("Stall Rent", cut.Markup), RenderTimeout);
        Assert.Empty(cut.FindAll("[role='dialog']"));
        Assert.DoesNotContain("STALL_RENT", cut.Find(".rev-group").TextContent);   // internal references stay in the details
        ClickButton(cut, "Details");

        cut.WaitForAssertion(() =>
        {
            var drawer = cut.Find(".v3-drawer[role='dialog']");
            Assert.Equal("true", drawer.GetAttribute("aria-modal"));
            Assert.Contains("STALL_RENT", drawer.TextContent);
            Assert.Single(drawer.QuerySelectorAll(".rev-history-table tbody tr"));
            Assert.Empty(drawer.QuerySelectorAll("footer button"));
            Assert.Contains("is-selected", cut.Find(".rev-row").ClassName);
        }, RenderTimeout);

        cut.Find(".v3-drawer").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")), RenderTimeout);
    }

    [Fact]
    public void ListFailure_ShowsRetryAndRetryLoadsAgain()
    {
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.SetupSequence(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Failure("Server unavailable", 500))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Success([]));
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<RevenueSetup>();
        cut.WaitForAssertion(() => Assert.Contains("Couldn’t load revenue sources", cut.Markup), RenderTimeout);
        ClickButton(cut, "Try again");
        cut.WaitForAssertion(() => Assert.Contains("No revenue sources configured yet.", cut.Markup), RenderTimeout);
        api.Verify(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("SuperAdmin", true)]
    [InlineData("Admin", false)]
    public void Settings_OnlyOffersRevenueSourcesLinkToHead(string role, bool actionable)
    {
        RegisterSettingsPageServices();
        this.AddTestAuthorization().SetAuthorized("staff").SetRoles(role);
        var cut = RenderComponent<Settings>();

        cut.WaitForAssertion(() => Assert.Contains("Revenue Sources", cut.Markup), RenderTimeout);
        var revenueLinks = cut.FindAll("a[href='/settings/revenue']");
        if (actionable)
        {
            Assert.Single(revenueLinks);
            Assert.Contains("Revenue Sources", revenueLinks[0].TextContent);
        }
        else
        {
            Assert.Empty(revenueLinks);
            Assert.Contains("Managed by the MEEDO Head", cut.Markup);
            Assert.Contains("aria-disabled=\"true\"", cut.Markup);
        }
    }

    [Fact]
    public void Settings_UsesTenantPayload_WithoutClaimingAnOpenCollectionPeriod()
    {
        var payload = new SystemSettingsDto(
            new OfficeProfileDto("Coastal Enterprise Office", "Test Municipality", "Test Province", "StallTrack", "Coastal Office"),
            new SecurityPolicyDto(20, 3, 4, 25, ["Head", "Admin", "Collector"]),
            new CollectionRulesDto(2, 18, 4, "Philippine Standard Time (UTC+8)"),
            new SystemInfoDto("StallTrack", "9.8.7", "Synthetic", "UTC+8", new DateTime(2027, 2, 15)),
            [new FacilityRuleDto("Custom1", "Coastal Hall", "Monthly rental", "Per stall contract", "Monthly")]);
        RegisterSettingsPageServices(Task.FromResult(Result<SystemSettingsDto>.Success(payload)));
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        var cut = RenderComponent<Settings>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Coastal Enterprise Office", cut.Markup);
            Assert.Contains("Test Municipality", cut.Markup);
            var figures = cut.FindAll(".v3h-val").Select(v => v.TextContent).ToArray();
            Assert.Equal(new[] { "Feb 2027", "v9.8.7" }, figures);
            Assert.Contains("4 failed attempts · 25 minutes", cut.Find(".set-grid").TextContent);
            Assert.DoesNotContain("System configuration · read-only", cut.FindComponent<EEMOCantilanSDS.Client.Components.Shared.V3Hero>().Markup);
            Assert.DoesNotContain("Collection Period", cut.Markup);
            Assert.Empty(cut.FindAll(".pill-ok"));
            Assert.DoesNotContain("Contract expiry", cut.Markup);
            Assert.DoesNotContain("Azure PITR", cut.Markup);
            Assert.DoesNotContain("15 yours", cut.Markup);
            Assert.Single(cut.FindAll("a[href='/settings/facilities']"));
        }, RenderTimeout);
    }

    [Fact]
    public void Settings_LoadingAndFailure_DoNotInventCountsDatesOrVersion()
    {
        var response = new TaskCompletionSource<Result<SystemSettingsDto>>();
        RegisterSettingsPageServices(response.Task);
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        var cut = RenderComponent<Settings>();
        cut.WaitForAssertion(() => Assert.Equal(new[] { "—", "—" },
            cut.FindAll(".v3h-val").Select(v => v.TextContent).ToArray()), RenderTimeout);

        response.SetResult(Result<SystemSettingsDto>.Failure("Unavailable", 500));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Couldn't load settings", cut.Markup);
            Assert.Equal(new[] { "—", "—" },
                cut.FindAll(".v3h-val").Select(v => v.TextContent).ToArray());
            Assert.Single(cut.FindAll(".set-retry"));
        }, RenderTimeout);
    }

    [Fact]
    public async Task ApiClient_FormatsAsOfDateInvariantly()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            HttpRequestMessage? captured = null;
            var handler = new CapturingHandler(request =>
            {
                captured = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://stalltrack.test/") };
            var client = new RevenueClassificationsApiClient(http);

            var result = await client.GetClassificationsAsync(new DateOnly(2026, 9, 30));

            Assert.True(result.IsSuccess);
            Assert.Equal("?asOf=2026-09-30", captured?.RequestUri?.Query);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private (IRenderedComponent<RevenueSetup> Cut, Mock<IRevenueClassificationsApiClient> Api) RenderPage(
        IReadOnlyList<RevenueClassificationDto> items,
        Guid? historyId = null,
        IReadOnlyList<RevenueClassificationPolicyDto>? history = null)
    {
        var api = new Mock<IRevenueClassificationsApiClient>();
        api.Setup(client => client.GetClassificationsAsync(It.IsAny<DateOnly?>()))
            .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationDto>>.Success(items));
        if (historyId.HasValue)
            api.Setup(client => client.GetPolicyHistoryAsync(historyId.Value))
                .ReturnsAsync(Result<IReadOnlyList<RevenueClassificationPolicyDto>>.Success(history ?? []));
        Services.AddSingleton(api.Object);
        RegisterGlobalPageServices();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        return (RenderComponent<RevenueSetup>(), api);
    }

    private void RegisterGlobalPageServices()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        var municipalities = new Mock<IMunicipalitiesApiClient>();
        municipalities.Setup(api => api.GetCurrentBrandingAsync())
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("Unavailable", 500));
        Services.AddSingleton(new BrandingState(municipalities.Object));
    }

    private void RegisterSettingsPageServices(Task<Result<SystemSettingsDto>>? settingsResponse = null)
    {
        RegisterGlobalPageServices();
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(api => api.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success([]));
        Services.AddSingleton(facilities.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));

        var settings = new Mock<ISettingsApiClient>();
        settings.Setup(api => api.GetSystemSettingsAsync()).Returns(settingsResponse ?? Task.FromResult(Result<SystemSettingsDto>.Success(
            new SystemSettingsDto(
                new OfficeProfileDto("MEEDO", "Cantilan", "Surigao del Sur", "StallTrack", "MEEDO Head"),
                new SecurityPolicyDto(15, 7, 5, 15, ["SuperAdmin", "Admin"]),
                new CollectionRulesDto(1, 12, 3, "Asia/Manila"),
                new SystemInfoDto("StallTrack", "1.0", "Test", "Asia/Manila", DateTime.UtcNow),
                []))));
        Services.AddSingleton(settings.Object);

        var mfa = new Mock<IMfaApiClient>();
        mfa.Setup(api => api.GetMfaStatusAsync()).ReturnsAsync(Result<MfaStatusDto>.Success(
            new MfaStatusDto(false, false, null, 0)));
        Services.AddSingleton(mfa.Object);
        Services.AddSingleton<ITenantUsageApiClient>(Mock.Of<ITenantUsageApiClient>(api =>
            api.GetUsageAsync() == Task.FromResult(Result<EEMOCantilanSDS.Application.Dtos.SystemHealth.TenantUsageDto>.Failure("Unavailable", 500))));
        Services.AddSingleton<IDatabaseHealthApiClient>(Mock.Of<IDatabaseHealthApiClient>(api =>
            api.GetHealthAsync() == Task.FromResult(Result<EEMOCantilanSDS.Application.Dtos.SystemHealth.DatabaseHealthDto>.Failure("Unavailable", 500))));

        Services.AddLogging();
        Services.AddSingleton<AuthService>(provider => new AuthService(
            provider.GetRequiredService<IJSRuntime>(),
            provider.GetRequiredService<NavigationManager>(),
            new AuthStateProvider(new HttpContextAccessor()),
            new TokenService(),
            provider.GetRequiredService<ILogger<AuthService>>()));
    }

    private static RevenueClassificationDto Classification(
        string code,
        string displayName,
        bool active,
        bool hasPolicyVersions,
        RevenueClassificationPolicyDto? effectivePolicy,
        Guid? id = null) => new(id ?? Guid.NewGuid(), code, active, hasPolicyVersions, effectivePolicy);

    private static RevenueClassificationPolicyDto Policy(
        string code,
        string displayName,
        DateOnly effectiveDate,
        RevenueInstrumentType? instrument,
        Guid? id = null) => new(id ?? Guid.NewGuid(), effectiveDate, displayName, null, instrument,
            new DateTime(2026, 9, 23, 4, 0, 0, DateTimeKind.Utc), "head@eemo.test");

    private static void ClickButton(IRenderedComponent<RevenueSetup> cut, string text) =>
        cut.FindAll("button").First(button => button.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    private static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
