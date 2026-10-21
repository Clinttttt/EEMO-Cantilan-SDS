using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The Fish / Meat Vendor Holder List: the eleven-column registration identity list, independent of any month's collections.</summary>
public sealed class FishMeatVendorHolderListTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IOfficeSourcesApiClient> _api = new();

    public FishMeatVendorHolderListTests()
    {
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    [Fact]
    public void ListsTheElevenColumns_WithDashesForMissingFields_AndTheRouteStaysOfficeOnly()
    {
        Assert.Equal("/operations/fish-meat-vendor-fees/holder-list",
            Assert.Single(typeof(FishMeatVendorHolderList).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        var cut = Render(Serve());
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["#", "Business name", "Last name", "First name", "Middle name", "Extname", "Sex", "Business Address", "Type Of", "Tax year", "Nature of business"],
                cut.FindAll(".lsh-table thead th").Select(t => t.TextContent.Trim()).ToArray());
            Assert.Equal(2, cut.FindAll(".lsh-table tbody tr").Count);
            var bare = cut.FindAll(".lsh-table tbody tr").Single(r => r.TextContent.Contains("Bare Fish"));
            Assert.Equal(11, bare.Children.Length);
            Assert.Equal("—", bare.Children[2].TextContent.Trim());                // last name missing → em dash, DisplayName is never split
            Assert.Equal("New", bare.Children[8].TextContent.Trim());              // Type Of = registration kind, not Fish/Meat
        }, Timeout);
    }

    [Fact]
    public void TypeFilterAndSearchNarrowTheRows_AndTheCsvFollowsThem()
    {
        var cut = Render(Serve());
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".lsh-table tbody tr").Count), Timeout);
        cut.FindAll(".lsh-seg-item").Single(b => b.TextContent.Trim() == "Meat").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".lsh-table tbody tr")), Timeout);
        var csv = Uri.UnescapeDataString(cut.Find(".lsh-actions a[download]").GetAttribute("href")!);
        Assert.Contains("Joji Meat", csv);
        Assert.DoesNotContain("Bare Fish", csv);
    }

    private IRenderedFragment Render(object _) => RenderComponent<FishMeatVendorHolderList>();

    private object Serve()
    {
        _api.Setup(x => x.ManageRegistrationsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<VendorRegistrationStatus?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<VendorRegistryManagement>.Success(new VendorRegistryManagement(2026, 2026, 10, [Row("Joji Meat", "Hepol", "Joji", FishMeatVendorType.Meat), Row("Bare Fish", null, null, FishMeatVendorType.Fish)], 0m)));
        return this;
    }

    private static VendorRegistrationManagementRow Row(string business, string? last, string? first, FishMeatVendorType type) => new(new FishMeatVendorRegistrationDto(Guid.NewGuid(), 2026, type, VendorRegistrationKind.New, business, business, last is null ? null : "San Vicent", null, last, first),
        VendorRegistrationStatus.Active, "head", DateTime.UtcNow, null, null, null, null, null, 0m, 0m);
}
