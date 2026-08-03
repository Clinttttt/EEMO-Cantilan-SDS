using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using AddVendorModal = EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal;

/// <summary>
/// bUnit render tests for the handover offer on the Add / Edit vendor form.
///
/// The offer used to be a strip at the very foot of a long, scrolling drawer: the clerk typed a number that
/// already belonged to a vacated stall, pressed Add, and nothing appeared to happen unless they scrolled to
/// the bottom to find it. It is now asked as a dialog over the form, which is what these tests pin — that it
/// appears without scrolling, that declining it leaves the form alone, and that a fresh offer asks again.
/// </summary>
public class AddVendorModalReuseOfferTests : TestContext
{
    private const string Offer =
        "Stall 12 was closed on Mar 3, 2026. You can assign it to this lessee instead of using a new number.";

    private IRenderedComponent<AddVendorModal> RenderForm(string? offer, EventCallback? onConfirm = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;   // the drawer scrolls its body via JS on render

        // The global _Imports.razor injects these into every component; stub them so the form resolves.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        var form = new AddVendorModal.VendorModalForm { FacilityCode = "TCC", StallNo = "12" };

        return RenderComponent<AddVendorModal>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.IsEditing, false);
            p.Add(c => c.Form, form);
            p.Add(c => c.ReuseOfferMessage, offer);
            if (onConfirm is not null) p.Add(c => c.OnReuseConfirm, onConfirm.Value);
        });
    }

    [Fact]
    public void Offer_IsAskedAsADialog_NotAStripAtTheFootOfTheForm()
    {
        var cut = RenderForm(Offer);

        Assert.Single(cut.FindAll(".avm-nested-dialog"));
        Assert.Contains(Offer, cut.Markup);
        Assert.Contains("Stall 12 is already registered", cut.Markup);
        // The old foot-of-form strip must not come back.
        Assert.Empty(cut.FindAll(".avm-reuse"));
    }

    [Fact]
    public void NoOffer_ShowsNoDialog()
    {
        var cut = RenderForm(offer: null);

        Assert.Empty(cut.FindAll(".avm-nested-dialog"));
    }

    [Fact]
    public void Declining_ClosesTheDialog_AndLeavesTheFormOpen()
    {
        var cut = RenderForm(Offer);

        cut.Find(".avm-nested-foot .btn-ghost").Click();

        Assert.Empty(cut.FindAll(".avm-nested-dialog"));
        Assert.Single(cut.FindAll(".eemo-drawer"));      // the form itself is untouched
    }

    [Fact]
    public void Accepting_RaisesTheHandover_Once()
    {
        var confirmed = 0;
        var cut = RenderForm(Offer, EventCallback.Factory.Create(this, () => confirmed++));

        cut.Find(".avm-nested-foot .btn-primary").Click();

        Assert.Equal(1, confirmed);
        Assert.Empty(cut.FindAll(".avm-nested-dialog"));
    }

    [Fact]
    public void Escape_DeclinesTheOffer()
    {
        var cut = RenderForm(Offer);

        cut.Find(".avm-nested-dialog").KeyDown(Key.Escape);

        Assert.Empty(cut.FindAll(".avm-nested-dialog"));
        Assert.Single(cut.FindAll(".eemo-drawer"));      // the form itself is untouched
    }

    [Fact]
    public void ADifferentOffer_AsksAgain()
    {
        var cut = RenderForm(Offer);
        cut.Find(".avm-nested-foot .btn-ghost").Click();
        Assert.Empty(cut.FindAll(".avm-nested-dialog"));

        // The clerk typed another number that is also reusable — a dismissed answer must not silence it.
        cut.SetParametersAndRender(p => p.Add(c => c.ReuseOfferMessage,
            "Stall 13's contract lapsed on Jan 31, 2026. You can assign it to this lessee."));

        Assert.Single(cut.FindAll(".avm-nested-dialog"));
    }
}
