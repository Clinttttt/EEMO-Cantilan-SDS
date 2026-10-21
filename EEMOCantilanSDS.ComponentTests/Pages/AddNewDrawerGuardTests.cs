using Bunit;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The shared Add / Edit drawer: one dismiss control (the header X) when the host says so, and an opt-in question before a changed form is discarded.</summary>
public sealed class AddNewDrawerGuardTests : TestContext
{
    public AddNewDrawerGuardTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
    }

    private IRenderedComponent<AddNewDrawer> Open(bool showCancel, bool guard, Action onCancel, object? session = null) =>
        RenderComponent<AddNewDrawer>(p => p
            .Add(x => x.Show, true).Add(x => x.Title, "Add Occupant & Space").Add(x => x.FormId, "f").Add(x => x.ShowCancel, showCancel).Add(x => x.GuardUnsaved, guard)
            .Add(x => x.SessionKey, session).Add(x => x.OnCancel, EventCallback.Factory.Create(this, onCancel))
            .Add(x => x.ChildContent, (RenderFragment)(b => { b.OpenElement(0, "input"); b.AddAttribute(1, "id", "x"); b.CloseElement(); })));

    [Fact]
    public void WithoutACancelButton_TheHeaderCloseIsTheOnlyDismissControl_AndAnUntouchedFormClosesAtOnce()
    {
        var closed = 0;
        var cut = Open(showCancel: false, guard: true, () => closed++);
        Assert.DoesNotContain(cut.FindAll("footer button"), b => b.TextContent.Trim() == "Cancel");
        Assert.Equal("Add Occupant & Space", cut.Find(".eemo-drawer-header-title").TextContent);
        cut.Find(".eemo-drawer-header-close").Click();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void AChangedFormAsksBeforeItIsDiscarded_AndKeepEditingLeavesItOpen()
    {
        var closed = 0;
        var cut = Open(showCancel: false, guard: true, () => closed++);
        cut.Find("#x").Input("typed");
        cut.Find(".eemo-drawer-header-close").Click();
        Assert.Equal(0, closed);
        Assert.Contains("unsaved changes", cut.Find("[role='alertdialog']").TextContent);
        Assert.Equal(["Keep editing", "Discard changes"], cut.FindAll("footer button").Select(b => b.TextContent.Trim()).ToArray());
        cut.FindAll("footer button").Single(b => b.TextContent.Trim() == "Keep editing").Click();
        Assert.Empty(cut.FindAll("[role='alertdialog']"));
        cut.Find(".eemo-drawer-header-close").Click();
        cut.FindAll("footer button").Single(b => b.TextContent.Trim() == "Discard changes").Click();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void ADrawerThatDoesNotOptIn_KeepsItsCancelAndClosesWithoutAsking()
    {
        var closed = 0;
        var cut = Open(showCancel: true, guard: false, () => closed++);
        cut.Find("#x").Input("typed");
        cut.FindAll("footer button").Single(b => b.TextContent.Trim() == "Cancel").Click();
        Assert.Equal(1, closed);
        Assert.Empty(cut.FindAll("[role='alertdialog']"));
    }
}
