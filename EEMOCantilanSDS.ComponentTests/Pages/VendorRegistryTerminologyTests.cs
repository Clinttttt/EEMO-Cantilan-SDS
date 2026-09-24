using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Components.Pages.Shared;
using EEMOCantilanSDS.Client.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class VendorRegistryTerminologyTests : TestContext
{
    [Fact]
    public void PermanentRegistryKeepsItsRouteAndAuthorizationWithoutAddingBusinessRoutes()
    {
        var routes = typeof(Vendor).GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .Select(route => route.Template)
            .ToArray();

        Assert.Equal(new[] { "/vendors" }, routes);

        var authorize = Assert.Single(typeof(Vendor)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("SuperAdmin,Admin", authorize.Roles);

        var allRoutes = typeof(Vendor).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                .Cast<RouteAttribute>())
            .Select(route => route.Template)
            .ToArray();
        Assert.DoesNotContain("/accounts/{id}", allRoutes);
        Assert.DoesNotContain("/payors", allRoutes);
    }

    [Fact]
    public void RegistryCopyUsesOccupantTermsAndRetainsDataAndActions()
    {
        var source = ReadWorkspaceFile("EEMOCantilanSDS.Client", "Components", "Pages", "Menus", "Vendor.razor");
        var codeStart = source.IndexOf("@code", StringComparison.Ordinal);
        var markup = codeStart >= 0 ? source[..codeStart] : source;
        markup = Regex.Replace(markup, "<!--[\\s\\S]*?-->", string.Empty);

        Assert.Contains("StallTrack — Spaces &amp; Occupants", markup);
        Assert.Contains("Occupancy Registry", markup);
        Assert.Contains("Total Spaces", markup);
        Assert.Contains("billable spaces", markup);
        Assert.Contains("Add Occupant &amp; Space", markup);
        Assert.Contains("Occupant &amp; Contract Information", markup);
        Assert.Contains("Edit Occupant &amp; Space", markup);
        Assert.Contains("UseOccupantTerminology=\"true\"", markup);

        Assert.DoesNotContain("Vendors &amp; Stalls", markup);
        Assert.DoesNotContain("Vendor Registry", markup);
        Assert.DoesNotContain("Total Vendors", markup);
        Assert.DoesNotContain("Add Vendor", markup);
        Assert.DoesNotContain("Vendor Information", markup);
        Assert.DoesNotContain("Edit Vendor", markup);

        Assert.Contains("@v.ActualOccupant", markup);
        Assert.Contains("v.StallNo", markup);
        Assert.Contains("@onclick=\"OpenCreate\"", markup);
        Assert.Contains("OpenDetail(v)", markup);
        Assert.Contains("OpenEdit(v)", markup);
        Assert.Contains("ViewProfile(v)", markup);
        Assert.Contains("OpenHistory(v)", source);
        Assert.Contains("SaveVendor", source);
        Assert.Contains("Nav.NavigateTo($\"/profile/{v.FacilityCode.ToLower()}/{v.StallGuid}\")", source);
    }

    [Fact]
    public void OccupancyHostShowsOccupantLanguageInAddModal()
    {
        var add = RenderModal(useOccupantTerminology: true, isEditing: false);
        Assert.Equal("Add Occupant & Space", add.Find(".eemo-drawer-header-title").TextContent.Trim());
        Assert.Equal("Register a rental space and its current occupant.", add.Find(".eemo-drawer-header-sub").TextContent.Trim());
        Assert.Equal("Occupant Information", add.Find(".avm-section-label").TextContent.Trim());
        Assert.Equal("Occupant *", add.FindAll(".avm-label")
            .Single(label => label.TextContent.Contains("Occupant", StringComparison.Ordinal))
            .TextContent.Trim());
        Assert.Equal("Add Occupant & Space", add.Find(".eemo-drawer-footer .btn-primary").TextContent.Trim());
    }

    [Fact]
    public void OccupancyHostUsesOccupantLanguageInEditModal()
    {
        var edit = RenderModal(useOccupantTerminology: true, isEditing: true);
        Assert.Equal("Edit Occupant & Space", edit.Find(".eemo-drawer-header-title").TextContent.Trim());
        Assert.Equal("Update the occupant and space information.", edit.Find(".eemo-drawer-header-sub").TextContent.Trim());
        Assert.Equal("Save Changes", edit.Find(".eemo-drawer-footer .btn-primary").TextContent.Trim());
    }

    [Fact]
    public void SharedModalKeepsItsExistingCopyForOtherHosts()
    {
        var cut = RenderModal(useOccupantTerminology: false, isEditing: false);

        Assert.Equal("Add New Vendor", cut.Find(".eemo-drawer-header-title").TextContent.Trim());
        Assert.Equal("Leasee Information", cut.Find(".avm-section-label").TextContent.Trim());
        Assert.Equal("Actual Occupant (Leasee) *", cut.FindAll(".avm-label")
            .Single(label => label.TextContent.Contains("Leasee", StringComparison.Ordinal))
            .TextContent.Trim());
        Assert.Equal("Add Vendor", cut.Find(".eemo-drawer-footer .btn-primary").TextContent.Trim());
    }

    [Fact]
    public void TpmPageRetainsTemporaryVendorTerminology()
    {
        var source = ReadWorkspaceFile("EEMOCantilanSDS.Client", "Components", "Pages", "Menus", "Facilities", "TPM.razor");

        Assert.Contains("@page \"/tpm\"", source);
        Assert.Contains("Vendor Attendance", source);
        Assert.Contains("Vendor Name", source);
        Assert.Contains("Add Vendor", source);
    }

    private IRenderedComponent<AddVendorModal> RenderModal(bool useOccupantTerminology, bool isEditing)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(new BrandingState(Mock.Of<IMunicipalitiesApiClient>()));
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());

        return RenderComponent<AddVendorModal>(parameters => parameters
            .Add(component => component.Show, true)
            .Add(component => component.IsEditing, isEditing)
            .Add(component => component.UseOccupantTerminology, useOccupantTerminology)
            .Add(component => component.Form, new AddVendorModal.VendorModalForm
            {
                FacilityCode = "TCC",
                StallNo = "12"
            }));
    }

    private static string ReadWorkspaceFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EEMOCantilanSDS.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(pathParts).ToArray()));
    }
}
