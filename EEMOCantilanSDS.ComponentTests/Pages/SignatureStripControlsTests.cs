using System.Text.RegularExpressions;
using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// What a Head can reach FROM THE SHEET, and what belongs in the modal instead.
///
/// <para>
/// A sheet is an official document, so it should carry as little furniture as possible. The per-line pencils have always
/// been the way in; the one thing that cannot live inside a line is the way back when there are no lines left, because it
/// would vanish with the last one. Everything else - adding, alignment, restoring the office trio - is in the modal.
/// </para>
/// </summary>
public class SignatureStripControlsTests : TestContext
{
    private const string CssPath =
        "../../../../EEMOCantilanSDS.Client/Components/Shared/SignatureStrip.razor.css";

    private const string OneLine =
        "{\"Align\":\"left\",\"Lines\":[{\"Caption\":\"Prepared by\",\"Name\":\"Ana Reyes\"}]}";

    private const string NoLines = "{\"Align\":\"left\",\"Lines\":[]}";

    /// <summary>Renders the strip for a Head, with the given value already stored for this LGU.</summary>
    private IRenderedComponent<SignatureStrip> Render(string? stored)
    {
        var branding = new BrandingState(Mock.Of<IMunicipalitiesApiClient>());
        branding.Apply(new MunicipalityBrandingDto(
            Code: "CANTILAN", TenantCode: "cantilan", Name: "Cantilan", Province: "Surigao del Sur",
            OfficeName: "Economic Enterprise & Management Office", SealPath: null,
            Status: "Active", IsActive: true, OfficeAcronym: "EEMO", Address: null,
            ReportSignatories: stored));

        Services.AddSingleton(branding);
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());

        // Components/_Imports.razor injects these into EVERY component under Components/, so the strip needs them even
        // though it uses none of them.
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());

        this.AddTestAuthorization().SetAuthorized("cly.sullano").SetRoles("SuperAdmin");

        return RenderComponent<SignatureStrip>();
    }

    private static string CentredRule() =>
        Regex.Match(File.ReadAllText(CssPath), @"\.sig-strip--center\s*\{[^}]*\}").Value;

    [Fact]
    public void TheSheetCarriesNoAlignmentButton()
    {
        // It used to sit on the finished document as a button labelled "Centre", beside the signatories it was about.
        var cut = Render(null);

        Assert.DoesNotContain(">Centre<", cut.Markup);
        Assert.DoesNotContain("Align left", cut.Markup);
        Assert.DoesNotContain("Restore default", cut.Markup);
        Assert.Empty(cut.FindAll(".sig-tools"));
    }

    [Fact]
    public void AlignmentIsOfferedInTheModal()
    {
        var cut = Render(null);

        cut.FindAll(".sig-edit")[0].Click();

        var links = cut.FindAll(".sig-link").Select(l => l.TextContent.Trim()).ToList();
        Assert.Contains(links, l => l.Contains("Centre the strip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnAlreadyCentredStripIsOfferedTheWayBack()
    {
        var cut = Render("{\"Align\":\"center\",\"Lines\":[{\"Caption\":\"Prepared by\",\"Name\":\"Ana Reyes\"}]}");

        cut.FindAll(".sig-edit")[0].Click();

        var links = cut.FindAll(".sig-link").Select(l => l.TextContent.Trim()).ToList();
        Assert.Contains(links, l => l.Contains("to the left", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WithSignatoriesOnTheSheetThereIsNoAddPencilBesideThem()
    {
        // The pencils beside the lines are the way in, so a second add control would be furniture on a document.
        var cut = Render(OneLine);

        Assert.Empty(cut.FindAll(".sig-tools"));
        Assert.Single(cut.FindAll(".sig-edit"));      // the line's own pencil, which is all a sheet needs
    }

    [Fact]
    public void WithNoSignatoriesLeftTheAddPencilIsTheWayBack()
    {
        // Nothing to open a modal from once the lines are gone, so this is the only route back - and the reason an office
        // can now choose to print no footer at all without being stranded.
        var cut = Render(NoLines);

        Assert.Single(cut.FindAll(".sig-tools"));
        Assert.Contains("Add a signatory", cut.Markup);
        Assert.Contains("no-print", cut.Find(".sig-tools").GetAttribute("class"));
    }

    [Fact]
    public void CentringSpansTheWHOLEFooterRatherThanOneColumn()
    {
        // THE BUG. The hosts are `grid-template-columns: repeat(3, 1fr)`, so the moment the strip has a box it is one
        // grid item in one of three columns - and centring inside a third of the sheet is not centring. Asserted against
        // the stylesheet because scoped CSS is not visible to a bUnit render.
        var centred = CentredRule();

        Assert.Contains("grid-column: 1 / -1", centred);      // spans every column of the grid footers
        Assert.Contains("flex: 1 1 100%", centred);           // and the full row of the flex ones
        Assert.Contains("justify-content: center", centred);
    }

    [Fact]
    public void ABoxedStripDoesNotDrawTheHostsRuleAcrossTheFooter()
    {
        // The hosts style `.print-report-signatures div`, which a `display: contents` strip never picked up. With a box
        // it would draw a rule the width of the sheet above the signatories.
        var centred = CentredRule();

        Assert.Contains("border-top: 0", centred);
        Assert.Contains("padding-top: 0", centred);
    }

    [Fact]
    public void TheDefaultStripStillLetsEachHostLayItOut()
    {
        // Unchanged behaviour is the point: every sheet in the portal keeps the footer it prints today, and only an
        // office that asks for centring gets anything different.
        var basic = Regex.Match(File.ReadAllText(CssPath), @"\.sig-strip\s*\{[^}]*\}").Value;

        Assert.Contains("display: contents", basic);
    }
}
