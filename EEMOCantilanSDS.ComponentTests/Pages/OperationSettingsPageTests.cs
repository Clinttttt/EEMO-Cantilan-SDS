using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Settings.UpdateTenantOperationSetting;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The Head's operation settings page: Head-only, presentation-only, and honest about what the server refuses.</summary>
public sealed class OperationSettingsPageTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public OperationSettingsPageTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>Authorization is registered last: it initialises the provider, after which a TestContext refuses new registrations.</summary>
    private IRenderedComponent<OperationSettings> Cut()
    {
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        return RenderComponent<OperationSettings>();
    }

    private static Mock<IOperationSettingsApiClient> Api(bool adopted)
    {
        var api = new Mock<IOperationSettingsApiClient>();
        api.Setup(c => c.GetAsync()).ReturnsAsync(Result<TenantOperationSettingsDto>.Success(
            OperationsTenantConfigurationTests.SettingsApi(adopted, ["MARKET_FEES"]).Object.GetAsync().Result.Value!));
        return api;
    }

    [Fact]
    public void OnlyTheHeadMayOpenIt()
    {
        var authorize = Assert.Single(typeof(OperationSettings).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("SuperAdmin", authorize.Roles);
    }

    [Fact]
    public void ListsEveryOperationUnderItsGroup_AndSaysThisIsPresentationOnly()
    {
        Services.AddSingleton(Api(adopted: true).Object);

        var cut = Cut();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(TenantOperationCatalog.All.Count, cut.FindAll("li.os-row").Count);
            Assert.Contains("Income From Market", cut.Markup);
            Assert.Contains("Space Rental", cut.Markup);
            Assert.Contains("wording and listing only", cut.Markup);
            Assert.Contains("1 of 11 listed", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void ANotYetAdoptedOffice_IsToldNothingChangesUntilItSaves()
    {
        Services.AddSingleton(Api(adopted: false).Object);

        var cut = Cut();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("has not customised this list", cut.Markup);
            Assert.Contains("11 of 11 listed", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void Saving_SendsTheStableCodeListingAndName_NotALabelAsIdentity()
    {
        var api = Api(adopted: true);
        api.Setup(c => c.UpdateAsync(It.IsAny<string>(), It.IsAny<UpdateTenantOperationSettingRequest>()))
            .ReturnsAsync((string _, UpdateTenantOperationSettingRequest _) =>
                Result<TenantOperationSettingsDto>.Success(OperationsTenantConfigurationTests.SettingsApi(true, ["MARKET_FEES", "ECF"]).Object.GetAsync().Result.Value!));
        Services.AddSingleton(api.Object);
        var cut = Cut();
        cut.WaitForElement("li[data-operation='ECF']", Timeout);

        var row = cut.Find("li[data-operation='ECF']");
        row.QuerySelector("input[type=checkbox]")!.Change(true);
        row.QuerySelector("input[type=text]")!.Input("Kuryente");
        row.QuerySelector("form")!.Submit();

        api.Verify(c => c.UpdateAsync("ECF", It.Is<UpdateTenantOperationSettingRequest>(r =>
            r.IsListed && r.LocalDisplayName == "Kuryente" && r.ExpectedVersion == 1)), Times.Once);
    }

    [Fact]
    public void AServerRefusal_IsShownOnTheOperation_InTheServersOwnWords()
    {
        var api = Api(adopted: true);
        api.Setup(c => c.UpdateAsync(It.IsAny<string>(), It.IsAny<UpdateTenantOperationSettingRequest>()))
            .ReturnsAsync(Result<TenantOperationSettingsDto>.Failure("“Market Fees” is the standard name of another operation (Market Fees).", ResultStatus.Invalid));
        Services.AddSingleton(api.Object);
        var cut = Cut();
        cut.WaitForElement("li[data-operation='ECF']", Timeout);

        var row = cut.Find("li[data-operation='ECF']");
        row.QuerySelector("input[type=text]")!.Input("Market Fees");
        row.QuerySelector("form")!.Submit();

        cut.WaitForAssertion(() => Assert.Contains("standard name of another operation", cut.Find("li[data-operation='ECF'] .os-error").TextContent), Timeout);
    }

    [Fact]
    public void AnOperationWithACantilanOnlyRule_AsksTheHeadToConfirmIt_AndNoOtherOperationDoes()
    {
        var model = Api(adopted: true).Object.GetAsync().Result.Value!;
        var flagged = model with
        {
            Items = model.Items.Select(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL"
                ? i with { PolicyNotice = "Signed space contracts are recorded under the three-year standard adopted by Cantilan's office (MEEDO)." }
                : i).ToList(),
        };
        var api = new Mock<IOperationSettingsApiClient>();
        api.Setup(c => c.GetAsync()).ReturnsAsync(Result<TenantOperationSettingsDto>.Success(flagged));
        Services.AddSingleton(api.Object);

        var cut = Cut();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, cut.FindAll(".os-policy").Count);
            Assert.Contains("Policy confirmation required", cut.Find("li[data-operation=\"KANMANGGAY_SPACE_RENTAL\"] .os-policy").TextContent);
            Assert.Contains("three-year", cut.Find(".os-policy").TextContent);
        }, Timeout);
    }

    [Fact]
    public void WhenItCannotBeLoaded_ItSaysSo_AndOffersARetry()
    {
        var api = new Mock<IOperationSettingsApiClient>();
        api.Setup(c => c.GetAsync()).ReturnsAsync(Result<TenantOperationSettingsDto>.Failure("down", 503));
        Services.AddSingleton(api.Object);

        var cut = Cut();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("couldn't be loaded", cut.Markup);
            Assert.Contains("Try again", cut.Markup);
        }, Timeout);
    }
}
