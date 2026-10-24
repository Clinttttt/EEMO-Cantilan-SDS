using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Components.Pages.Shared;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OfficeProfilePage = EEMOCantilanSDS.Client.Components.Pages.Menus.OfficeProfile;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class OfficeProfileBrandingTests : TestContext
{
    private readonly Mock<IMunicipalitiesApiClient> _municipalities = new();
    private readonly Mock<ISettingsApiClient> _settings = new();
    private static MunicipalityBrandingDto Branding(string acronym) => new(
        Code: "CARRASCAL", TenantCode: "carrascal", Name: "Carrascal", Province: "Test province",
        OfficeName: "Carrascal enterprise office", SealPath: "https://api.test/carrascal/seal?v=1",
        Status: "Active", IsActive: true, OfficeAcronym: acronym);

    public OfficeProfileBrandingTests()
    {
        Services.AddSingleton(_municipalities.Object);
        Services.AddSingleton(_settings.Object);
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        var online = new Mock<IOnlinePaymentsApiClient>();
        online.Setup(x => x.GetAwaitingOrAsync()).ReturnsAsync(Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>.Success([]));
        Services.AddSingleton(online.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        _settings.Setup(x => x.GetOfficeProfileAsync()).ReturnsAsync(Result<OfficeProfileEditDto>.Success(
            new("Carrascal enterprise office", "OLD", null, Branding("OLD").SealPath, "Carrascal", "Test province")));
        _settings.Setup(x => x.UpdateOfficeProfileAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result<bool>.Success(true));
    }

    private static void Save(IRenderedComponent<OfficeProfilePage> page)
    {
        page.Find("input[maxlength='12']").Input("CLEE");
        page.FindAll("button").Single(x => x.TextContent.Contains("Save changes")).Click();
    }

    [Fact]
    public void SavingUpdatesTheExistingSidebarAndAccountChip_WithoutPageReload()
    {
        _municipalities.SetupSequence(x => x.GetCurrentBrandingAsync())
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Success(Branding("OLD")))
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Success(Branding("CLEE")));
        var page = RenderComponent<OfficeProfilePage>();
        var sidebar = RenderComponent<Sidebar>();
        var chip = RenderComponent<AccountChip>();
        sidebar.Find(".sidebar-seal img").TriggerEvent("onerror", new EventArgs());
        Save(page);
        page.WaitForAssertion(() => Assert.Contains("Office profile updated.", page.Find(".op-banner-ok").TextContent));
        chip.WaitForAssertion(() => Assert.Contains("CLEE Office", chip.Markup));
        sidebar.WaitForAssertion(() => Assert.Equal(Branding("CLEE").SealPath, sidebar.Find(".sidebar-seal img").GetAttribute("src")));
        _municipalities.Verify(x => x.GetCurrentBrandingAsync(), Times.Exactly(2));
    }

    [Fact]
    public void RejectedSaveDoesNotRefreshOrReplaceAuthoritativeBranding()
    {
        _municipalities.Setup(x => x.GetCurrentBrandingAsync()).ReturnsAsync(Result<MunicipalityBrandingDto>.Success(Branding("OLD")));
        _settings.Setup(x => x.UpdateOfficeProfileAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(Result<bool>.Failure("Rejected"));
        var page = RenderComponent<OfficeProfilePage>();
        Save(page);
        page.WaitForAssertion(() => Assert.Contains("Rejected", page.Find(".op-banner-error").TextContent));
        Assert.Equal("OLD", Services.GetRequiredService<BrandingState>().OfficeAcronym);
        _municipalities.Verify(x => x.GetCurrentBrandingAsync(), Times.Once);
    }

    [Fact]
    public async Task FailedRefreshDoesNotPretendTheSaveFailed_AndTheNextReadRecovers()
    {
        _municipalities.SetupSequence(x => x.GetCurrentBrandingAsync())
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Success(Branding("OLD")))
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("unavailable"))
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Success(Branding("CLEE")));
        var page = RenderComponent<OfficeProfilePage>();
        Save(page);
        page.WaitForAssertion(() => Assert.Contains("saved", page.Find(".op-banner-ok").TextContent, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("retry", page.Find(".op-banner-ok").TextContent, StringComparison.OrdinalIgnoreCase);
        await Services.GetRequiredService<BrandingState>().EnsureLoadedAsync();
        Assert.Equal("CLEE", Services.GetRequiredService<BrandingState>().OfficeAcronym);
        _settings.Verify(x => x.UpdateOfficeProfileAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Once);
    }
}
