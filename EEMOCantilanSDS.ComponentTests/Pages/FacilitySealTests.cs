using Bunit;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using FacilitySeal = EEMOCantilanSDS.Client.Components.Shared.FacilitySeal;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The facility's seal.
///
/// <para>Two things it must never do. It must never show one LGU the crest of another, and it must never leave the
/// ring hollow because a municipality has not uploaded a mark yet. Everything else about it is drawing.</para>
/// </summary>
public class FacilitySealTests : TestContext
{
    private bool _registered;

    private IRenderedComponent<FacilitySeal> Render(FacilityCode code, string? name = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Registered once. bUnit refuses new registrations after the first render, and one test renders two seals.
        if (!_registered)
        {
            _registered = true;

            // The Components folder's shared imports inject these into every component, so they must be present even
            // though the seal itself asks for none of them.
            Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
            Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
            Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
            Services.AddSingleton(Mock.Of<IStallsApiClient>());
            Services.AddSingleton(Mock.Of<ISetupApiClient>());
            Services.AddSingleton(Mock.Of<ISettingsApiClient>());
            Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
            Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        }

        return RenderComponent<FacilitySeal>(p =>
        {
            p.Add(c => c.Code, code);
            if (name is not null) p.Add(c => c.Name, name);
        });
    }

    [Fact]
    public void TheCentreCarriesTheFacilitysOwnMark()
    {
        var cut = Render(FacilityCode.NPM);

        // The facility's mark, not the municipality's crest: the crest belongs to the town, and a seal naming the
        // market while showing the town's arms says the wrong thing about which of the two the page is about.
        Assert.Contains("fseal-mark", cut.Markup);
        Assert.Contains("<svg", cut.Markup);
        Assert.DoesNotContain("img", cut.Markup);
    }

    [Fact]
    public void AFacilityWithNoArtworkFallsBackToTheTenantsOwnCrest()
    {
        // A facility a Head added for their own LGU has no mark of its own. The ring must not be left hollow, and the
        // crest that stands in comes from the tenant's branding - never another LGU's.
        var cut = Render(FacilityCode.Custom1);

        var crest = cut.Find("img.fseal-crest");
        Assert.NotEqual(string.Empty, crest.GetAttribute("src") ?? string.Empty);

        // Decorative: the facility is named in text beside every seal, so the image is not read out a second time.
        Assert.Equal(string.Empty, crest.GetAttribute("alt"));
    }

    [Fact]
    public void TheRingNamesTheFacility()
    {
        var cut = Render(FacilityCode.NPM, "New Public Market");

        Assert.Contains("NEW PUBLIC MARKET", cut.Markup);
        Assert.Contains("OFFICIAL SEAL", cut.Markup);
    }

    [Fact]
    public void ANameTooLongForTheArcGivesWayToTheShortCode()
    {
        // Left at full length the two ends of the text meet at the top of the circle and overlap, which reads as a
        // broken seal rather than a long name.
        var cut = Render(FacilityCode.TCC, "An Extremely Long Facility Name That Cannot Fit The Ring");

        Assert.DoesNotContain("EXTREMELY LONG FACILITY", cut.Markup);
        Assert.Contains("TCC", cut.Markup);
    }

    [Fact]
    public void TwoSealsOnOnePageDoNotShareAPath()
    {
        // The curved text is set on a path referenced by id. Shared ids would make the second seal draw its text on
        // the first one's arc, or not at all.
        var first = Render(FacilityCode.NPM);
        var second = Render(FacilityCode.TCC);

        var idOf = (IRenderedComponent<FacilitySeal> c) =>
            System.Text.RegularExpressions.Regex.Match(c.Markup, @"fseal-top-(\w+)").Groups[1].Value;

        Assert.NotEqual(string.Empty, idOf(first));
        Assert.NotEqual(idOf(first), idOf(second));
    }
}
