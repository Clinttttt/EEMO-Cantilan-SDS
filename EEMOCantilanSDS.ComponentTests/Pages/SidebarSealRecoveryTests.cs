using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Components.Pages.Shared;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class SidebarSealRecoveryTests : TestContext
{
    private const string SavedSeal = "data:image/jpeg;base64,/9j/4AAQSkZJRg==";
    private const string UrlSeal = "https://api.stalltrack.site/api/municipalities/carrascal/seal?v=unit";
    private readonly Mock<IMunicipalitiesApiClient> _branding = new();
    private readonly Mock<ISettingsApiClient> _settings = new();

    private static MunicipalityBrandingDto Carrascal() => new(
        Code: "CARRASCAL", TenantCode: "carrascal", Name: "Carrascal",
        Province: "Surigao del Sur", OfficeName: "Carrascal Local Economic Enterprise",
        SealPath: UrlSeal, Status: "Active", IsActive: true, OfficeAcronym: "CLEE");

    public SidebarSealRecoveryTests()
    {
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_branding.Object);
        Services.AddSingleton(_settings.Object);
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        var online = new Mock<IOnlinePaymentsApiClient>();
        online.Setup(a => a.GetAwaitingOrAsync())
            .ReturnsAsync(Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>.Success([]));
        Services.AddSingleton(online.Object);
        _settings.Setup(a => a.GetOfficeProfileAsync()).ReturnsAsync(
            Result<OfficeProfileEditDto>.Success(new("Carrascal Local Economic Enterprise",
                "CLEE", null, SavedSeal, "Carrascal", "Surigao del Sur")));
    }

    [Fact]
    public void Healthy_primary_seal_uses_versioned_url_without_extra_office_lookup()
    {
        _branding.Setup(a => a.GetCurrentBrandingAsync()).ReturnsAsync(
            Result<MunicipalityBrandingDto>.Success(Carrascal()));

        var sidebar = RenderComponent<Sidebar>();

        Assert.Equal(UrlSeal, sidebar.Find(".sidebar-seal img").GetAttribute("src"));
        _settings.Verify(a => a.GetOfficeProfileAsync(), Times.Never);
    }

    [Fact]
    public void Broken_primary_image_recovers_from_authenticated_office_profile()
    {
        _branding.Setup(a => a.GetCurrentBrandingAsync()).ReturnsAsync(
            Result<MunicipalityBrandingDto>.Success(Carrascal()));

        var sidebar = RenderComponent<Sidebar>();
        sidebar.Find(".sidebar-seal img").TriggerEvent("onerror", new EventArgs());

        sidebar.WaitForAssertion(() => Assert.Equal(SavedSeal,
            sidebar.Find(".sidebar-seal img").GetAttribute("src")));
        _settings.Verify(a => a.GetOfficeProfileAsync(), Times.Once);
    }

    [Fact]
    public void Failed_branding_read_can_use_authentic_office_seal_without_public_tenant_guessing()
    {
        _branding.Setup(a => a.GetCurrentBrandingAsync()).ReturnsAsync(
            Result<MunicipalityBrandingDto>.Failure("Temporary branding failure"));

        var sidebar = RenderComponent<Sidebar>();

        sidebar.WaitForAssertion(() => Assert.Equal(SavedSeal,
            sidebar.Find(".sidebar-seal img").GetAttribute("src")));
        _settings.Verify(a => a.GetOfficeProfileAsync(), Times.Once);
    }

    [Fact]
    public void Missing_authoritative_office_seal_never_uses_another_LGU_image()
    {
        _branding.Setup(a => a.GetCurrentBrandingAsync()).ReturnsAsync(
            Result<MunicipalityBrandingDto>.Failure("Temporary branding failure"));
        _settings.Setup(a => a.GetOfficeProfileAsync()).ReturnsAsync(
            Result<OfficeProfileEditDto>.Failure("Forbidden"));

        var sidebar = RenderComponent<Sidebar>();

        sidebar.WaitForAssertion(() => Assert.Equal(BrandingState.WaitingSealPath,
            sidebar.Find(".sidebar-seal img").GetAttribute("src")));
        _settings.Verify(a => a.GetOfficeProfileAsync(), Times.Once);
    }

    [Fact]
    public void Broken_authenticated_profile_image_falls_back_to_neutral_mark_without_looping()
    {
        _branding.Setup(a => a.GetCurrentBrandingAsync()).ReturnsAsync(
            Result<MunicipalityBrandingDto>.Failure("Temporary branding failure"));

        var sidebar = RenderComponent<Sidebar>();
        sidebar.WaitForAssertion(() => Assert.Equal(SavedSeal,
            sidebar.Find(".sidebar-seal img").GetAttribute("src")));
        sidebar.Find(".sidebar-seal img").TriggerEvent("onerror", new EventArgs());

        sidebar.WaitForAssertion(() => Assert.Equal(BrandingState.WaitingSealPath,
            sidebar.Find(".sidebar-seal img").GetAttribute("src")));
        _settings.Verify(a => a.GetOfficeProfileAsync(), Times.Once);
    }
}
