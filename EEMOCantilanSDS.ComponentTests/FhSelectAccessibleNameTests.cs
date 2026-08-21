using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// What the portal's own dropdown announces, and that it still renders the choices it is given.
///
/// <para>
/// The trigger shows the selected value, which is all a screen reader had to go on. Where the field's label is
/// printed outside the component — the market day drawer prints it above, the way the rest of that form does — a
/// row of these announced "Thursday, button" then "August 27 2026, button", with nothing saying which was the day
/// and which was the date it starts on. <c>AriaLabel</c> carries the field's own label in for that case.
/// </para>
///
/// <para>
/// The fallback to <c>Caption</c> is the part worth pinning: <c>Caption</c> is the visible label when a caller uses
/// one, and three pages already do, so every one of them must keep the name it had before the parameter existed.
/// </para>
/// </summary>
public class FhSelectAccessibleNameTests : TestContext
{
    private static readonly (string Value, string Label)[] Days =
    [
        ("Thursday", "Thursday"),
        ("Friday", "Friday"),
    ];

    /// <summary>
    /// The portal's <c>_Imports.razor</c> injects four services into every component in the tree, so even a
    /// dropdown that uses none of them will not render without them registered.
    /// </summary>
    public FhSelectAccessibleNameTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
    }

    [Fact]
    public void TheTriggerCarriesTheFieldsOwnLabel_WhenItIsPrintedOutside()
    {
        var cut = RenderComponent<FhSelect>(p => p
            .Add(s => s.Options, Days)
            .Add(s => s.Value, "Thursday")
            .Add(s => s.Caption, string.Empty)
            .Add(s => s.AriaLabel, "Held on"));

        Assert.Equal("Held on", cut.Find(".fh-dd-trigger").GetAttribute("aria-label"));
    }

    [Fact]
    public void TheCaptionIsStillTheNameForEveryCallerThatUsesOne()
    {
        var cut = RenderComponent<FhSelect>(p => p
            .Add(s => s.Options, Days)
            .Add(s => s.Value, "Thursday")
            .Add(s => s.Caption, "Month"));

        Assert.Equal("Month", cut.Find(".fh-dd-trigger").GetAttribute("aria-label"));
    }

    [Fact]
    public void WithNeither_NoEmptyNameIsRendered()
    {
        // An empty aria-label is worse than none: it names the control the empty string, which a screen reader
        // reads as an unlabelled button rather than falling back to the value shown on it.
        var cut = RenderComponent<FhSelect>(p => p
            .Add(s => s.Options, Days)
            .Add(s => s.Value, "Thursday")
            .Add(s => s.Caption, string.Empty));

        Assert.Null(cut.Find(".fh-dd-trigger").GetAttribute("aria-label"));
    }

    [Fact]
    public void EveryChoiceGivenIsOffered_AndTheSelectedOneIsMarked()
    {
        var cut = RenderComponent<FhSelect>(p => p
            .Add(s => s.Options, Days)
            .Add(s => s.Value, "Friday")
            .Add(s => s.AriaLabel, "Held on"));

        cut.Find(".fh-dd-trigger").Click();       // the panel renders only once opened

        var offered = cut.FindAll(".fh-dd-item .fh-dd-name").Select(n => n.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "Thursday", "Friday" }, offered);
        Assert.Equal("Friday", cut.Find(".fh-dd-item.active .fh-dd-name").TextContent.Trim());
    }
}
