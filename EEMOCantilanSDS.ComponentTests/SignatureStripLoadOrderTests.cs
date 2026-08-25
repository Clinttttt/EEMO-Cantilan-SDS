using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests;

using SignatureStrip = EEMOCantilanSDS.Client.Components.Shared.SignatureStrip;

/// <summary>
/// The signatory strip against the fault the office reported: a line they had removed came back whenever the page was
/// refreshed.
///
/// <para>
/// Nothing was wrong with the saving. A freshly loaded page renders once BEFORE branding returns, and at that moment every
/// branding accessor still answers with the office's defaults. The strip captured its lines there and never looked again, so
/// the LGU's own footer was replaced by the default one for the life of the page. This renders the strip with branding that
/// arrives late, which is what a real page load does.
/// </para>
/// </summary>
public class SignatureStripLoadOrderTests : TestContext
{
    private static MunicipalityBrandingDto Branding(string? signatories) => new(
        Code: "CANTILAN", TenantCode: "cantilan", Name: "Cantilan", Province: "Surigao del Sur",
        OfficeName: "Economic Enterprise & Management Office", SealPath: null,
        Status: "Active", IsActive: true, OfficeAcronym: "EEMO", Address: null,
        ReportSignatories: signatories);

    /// <summary>Renders the strip while the branding call is still in flight, then lets it complete.</summary>
    private IRenderedComponent<SignatureStrip> RenderWithLateBranding(string? stored)
    {
        var arrival = new TaskCompletionSource<Result<MunicipalityBrandingDto>>();

        var municipalities = new Mock<IMunicipalitiesApiClient>();
        municipalities.Setup(m => m.GetCurrentBrandingAsync()).Returns(arrival.Task);

        Services.AddSingleton(municipalities.Object);
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        // The shared _Imports injects these into every component; stub them so the strip resolves.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMfaApiClient>());
        Services.AddSingleton(new BrandingState(municipalities.Object));

        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("Cly Sullano");
        auth.SetRoles("SuperAdmin");

        var cut = RenderComponent<SignatureStrip>();

        // The office's own lines arrive after that first render, exactly as they do on a real page load.
        arrival.SetResult(Result<MunicipalityBrandingDto>.Success(Branding(stored)));

        return cut;
    }

    [Fact]
    public void TheOfficesOwnLinesSurviveAPageLoad()
    {
        // Two lines saved by the office, one of them not among the defaults.
        var cut = RenderWithLateBranding(
            "{\"Align\":\"left\",\"Lines\":[{\"Caption\":\"Prepared by\",\"Name\":\"Ana Reyes\"},{\"Caption\":\"Certified correct\",\"Name\":\"Cly Sullano\"}]}");

        cut.WaitForAssertion(() =>
        {
            var captions = cut.FindAll(".sig-caption").Select(c => c.TextContent.Trim()).ToList();
            Assert.Equal(2, captions.Count);
            Assert.Contains("Certified correct", captions);
            Assert.DoesNotContain(captions, c => c.Contains("Reviewed by", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AnOfficeThatWantsNoFooterKeepsNoneAcrossAPageLoad()
    {
        // The deliberate empty state. If the strip kept what the first render captured, a sheet the office cleared would
        // print the default lines again on every refresh.
        var cut = RenderWithLateBranding("{\"Align\":\"left\",\"Lines\":[]}");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".sig-caption")));
    }
}
