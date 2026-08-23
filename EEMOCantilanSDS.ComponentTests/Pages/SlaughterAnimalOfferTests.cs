using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using SlaughterRecordModal = EEMOCantilanSDS.Client.Components.Pages.Shared.SH.SlaughterRecordModal;

/// <summary>
/// Which animals a slaughterhouse offers, and at whose rates.
///
/// Reported by the office on 2026-08-23: the record dialog listed Hog, Carabao and Cow whatever the LGU's ordinance
/// says. Reading the code found worse than an over-long list. The per-head rates defaulted to Cantilan's ₱250 and ₱365
/// in three components and in the overview DTO; the overview resolved them with <c>Resolve()</c>, which reads an
/// unstated rate as zero, so an office that does not slaughter carabao was offered a carabao at ₱0 a head; and the
/// on-screen TOTAL was computed from the literals 250 and 365 while the cards displayed the office's own figures - so an
/// office charging ₱400 a hog was shown a total struck at ₱250, in front of the person about to confirm it.
///
/// The recording handler already refuses a transaction whose per-head rate is unstated. This is the same rule, one
/// screen earlier: an animal the office does not price is not offered at all.
/// </summary>
public class SlaughterAnimalOfferTests : TestContext
{
    private IRenderedComponent<SlaughterRecordModal> RenderModal(decimal? hog, decimal? large)
    {
        // The global _Imports.razor injects these into every component; stub them so the dialog resolves.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();

        return RenderComponent<SlaughterRecordModal>(p => p
            .Add(c => c.ShowModal, true)
            .Add(c => c.HogRate, hog)
            .Add(c => c.LargeRate, large));
    }

    [Fact]
    public void AnOfficeThatPricesBothIsOfferedBoth_AtItsOwnRates()
    {
        var cut = RenderModal(hog: 400m, large: 500m);
        var markup = cut.Markup;

        Assert.Contains("Hog", markup);
        Assert.Contains("Carabao", markup);
        Assert.Contains("Cow", markup);
        Assert.Contains("400 per head", markup);
        Assert.Contains("500 per head", markup);

        // Not the reference municipality's ordinance.
        Assert.DoesNotContain("250 per head", markup);
        Assert.DoesNotContain("365 per head", markup);
    }

    [Fact]
    public void AnAnimalTheOfficeDoesNotPriceIsNotOffered()
    {
        // Hogs only: no large-animal rate on file, so no carabao and no cow.
        var cut = RenderModal(hog: 400m, large: null);
        var markup = cut.Markup;

        Assert.Contains("Hog", markup);
        Assert.DoesNotContain("Carabao", markup);
        Assert.DoesNotContain("Cow", markup);
    }

    [Fact]
    public void AnOfficeWithNoRatesOnFileIsOfferedNoDefaultedAnimal()
    {
        var markup = RenderModal(hog: null, large: null).Markup;

        Assert.DoesNotContain("Hog", markup);
        Assert.DoesNotContain("Carabao", markup);
        Assert.DoesNotContain("Cow", markup);

        // The manual path stays: its rate is typed in by the person recording, so nothing is assumed.
        Assert.Contains("Other Animal Type", markup);
    }

    [Fact]
    public void NoScreenPricesAnAnimalFromALiteralOrdinance()
    {
        // Asserted on the source of all three dialogs, because the arithmetic is what was wrong: the cards read the
        // office's rate while the summary and the total read 250 and 365.
        foreach (var component in new[]
        {
            "SlaughterRecordModal.razor", "AddSlaughterTransactionModal.razor", "EditSlaughterTransactionModal.razor",
        })
        {
            var source = File.ReadAllText(Path.Combine(
                RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Pages", "Shared", "SH", component));

            Assert.DoesNotMatch(@"Heads \* 250", source);
            Assert.DoesNotMatch(@"Heads \* 365", source);
            Assert.DoesNotContain("= 250m;", source);
            Assert.DoesNotContain("= 365m;", source);
        }

        // And the office's own page passes its rates through without substituting a default.
        var page = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Pages", "Menus", "Facilities", "SH.razor"));

        Assert.DoesNotContain("?? 250m", page);
        Assert.DoesNotContain("?? 365m", page);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
