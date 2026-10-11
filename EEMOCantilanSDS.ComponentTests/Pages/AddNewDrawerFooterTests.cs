using Bunit;
using EEMOCantilanSDS.Client.Components.Shared;
using Microsoft.AspNetCore.Components;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public class AddNewDrawerFooterTests : TestContext
{
    public AddNewDrawerFooterTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
    }
    [Fact]
    public void OptionalLeadingActionPreservesDefaultFooterAndFormAssociation()
    {
        var canceled = 0;
        var submitted = 0;
        var cut = RenderComponent<AddNewDrawer>(p => p.Add(x => x.Show, true)
            .Add(x => x.Title, "Register").Add(x => x.FormId, "registration")
            .Add(x => x.OnCancel, () => canceled++).Add(x => x.OnSubmit, () => submitted++));
        Assert.Empty(cut.FindAll(".add-new-footer-leading"));
        Assert.Equal(new[] { "Cancel", "Save" }, cut.FindAll("footer button").Select(x => x.TextContent));
        Assert.Equal("registration", cut.Find("button[type='submit']").GetAttribute("form"));
        cut.Find("form").Submit();
        Assert.Equal(1, submitted);
        cut.Find("footer button[type='button']").Click();
        Assert.Equal(1, canceled);
        cut.SetParametersAndRender(p => p.Add(x => x.Saving, true));
        Assert.All(cut.FindAll("footer button"), x => Assert.True(x.HasAttribute("disabled")));
    }

    [Fact]
    public void LeadingNavigationIsOutsideFormAndBeforeCancelAndSubmit()
    {
        RenderFragment import = builder =>
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "type", "button");
            builder.AddContent(2, "Import list");
            builder.CloseElement();
        };
        var cut = RenderComponent<AddNewDrawer>(p => p.Add(x => x.Show, true)
            .Add(x => x.Title, "Register").Add(x => x.FormId, "registration")
            .Add(x => x.LeadingFooterActions, import));
        Assert.Equal(new[] { "Import list", "Cancel", "Save" }, cut.FindAll("footer button").Select(x => x.TextContent));
        Assert.Null(cut.Find(".add-new-footer-leading button").Closest("form"));
    }
}
