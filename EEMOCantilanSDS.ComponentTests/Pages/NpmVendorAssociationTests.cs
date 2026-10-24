using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;
public sealed class NpmVendorAssociationTests : TestContext
{
    private readonly Mock<IOfficeSourcesApiClient> _api = new();
    private const int Year = 2026;
    private readonly NpmVendorCandidate _source = new(Guid.NewGuid(), Guid.NewGuid(), "3", "Verified occupant", "Fish Area", true,
        new(2024, 1, 1), FishMeatVendorType.Fish, "source-version", [], []);
    private readonly FishMeatVendorRegistrationDto _vendor = new(Guid.NewGuid(), Year, FishMeatVendorType.Fish,
        VendorRegistrationKind.New, "Existing vendor", "Fish shop", null, null);
    public NpmVendorAssociationTests()
    {
        Services.AddSingleton(_api.Object); JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1, [_source])));
        _api.Setup(x => x.RegistrationsAsync(Year)).ReturnsAsync(Result<IReadOnlyList<FishMeatVendorRegistrationDto>>.Success([_vendor]));
    }
    private IRenderedComponent<NpmVendorRegistrationWorkflow> Open() => RenderComponent<NpmVendorRegistrationWorkflow>(p => p.Add(x => x.Show, true).Add(x => x.TaxYear, Year));
    // The first step follows the situation (no match: register from NPM; a similar name: link); a quiet switch changes it.
    private static void SwitchPath(IRenderedComponent<NpmVendorRegistrationWorkflow> cut) => cut.Find(".npm-link-switch").Click();
    private static void PickVendor(IRenderedComponent<NpmVendorRegistrationWorkflow> cut, string search) { cut.Find("#npm-vendor-search").Input(search); cut.Find(".npm-link-pick").Click(); }

    [Fact]
    public void Source_review_prefills_existing_drawer_without_inventing_business_or_address()
    {
        var cut = Open(); cut.Find(".npm-link-candidate").Click();
        var form = cut.FindComponent<VendorRegistrationDrawer>();
        Assert.True(form.Instance.Show);
        Assert.Empty(cut.FindAll(".npm-link-summary")); // eligible occupant skips the intermediate review
        Assert.Contains(_source.ContractId.ToString(), form.Markup);
        Assert.Empty(cut.FindAll("#npm-confirmation"));
        Assert.Empty(cut.FindAll("#npm-registration-evidence")); // source evidence is created automatically
        Assert.False(cut.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Empty(form.FindAll(".vendor-import-action button"));
        Assert.Null(form.Instance.Error); Assert.DoesNotContain("_error", cut.Markup);
        Assert.Equal("Verified occupant", form.Instance.Model.Name); Assert.Equal(FishMeatVendorType.Fish, form.Instance.Model.Type);
        Assert.True(string.IsNullOrEmpty(form.Instance.Model.Address)); Assert.True(string.IsNullOrEmpty(form.Instance.Model.Business));
        Export("npm-prefill", cut.Markup);
    }
    [Theory]
    [InlineData("Joji Hepol", "Joji", "", "Hepol")]
    [InlineData("Vincent E. Doloriel", "Vincent", "E.", "Doloriel")]
    [InlineData("Maria de la Cruz", "", "", "")] // compound family name cannot safely be inferred
    public void Npm_registration_prefills_only_unambiguous_identity(string occupant, string first, string middle, string last)
    {
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1, [_source with { Occupant = occupant }])));
        var cut = Open();
        cut.Find(".npm-link-candidate").Click();
        var form = cut.FindComponent<VendorRegistrationDrawer>().Instance.Model;
        Assert.Equal(occupant, form.Name);
        Assert.Equal(first, form.FirstName);
        Assert.Equal(middle, form.MiddleName);
        Assert.Equal(last, form.LastName);
        Assert.Equal("Fish retailing", form.NatureOfBusiness);
        Assert.Empty(cut.FindAll("#npm-registration-evidence"));
    }

    [Fact]
    public void Meat_registration_prefills_nature_from_verified_area_without_inventing_names()
    {
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1,
                [_source with { Occupant = "Joji Hepol", Area = "Meat Area", SuggestedType = FishMeatVendorType.Meat }])));
        var cut = Open();
        cut.Find(".npm-link-candidate").Click();
        var model = cut.FindComponent<VendorRegistrationDrawer>().Instance.Model;
        Assert.Equal(FishMeatVendorType.Meat, model.Type);
        Assert.Equal("Meat retailing", model.NatureOfBusiness);
        Assert.Equal("Joji", model.FirstName);
        Assert.Equal("Hepol", model.LastName);
    }

    [Fact]
    public void Linking_existing_registration_never_submits_registration_and_carries_exact_contract()
    {
        ConfirmNpmVendorAssociationRequest? sent = null;
        _api.Setup(x => x.LinkNpmVendorAsync(_vendor.Id, It.IsAny<ConfirmNpmVendorAssociationRequest>()))
            .Callback<Guid, ConfirmNpmVendorAssociationRequest>((_, r) => sent = r)
            .ReturnsAsync(Result<NpmVendorAssociationDto>.Failure("test", ResultStatus.Invalid));
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1, [_source with { PossibleMatches = [_vendor] }])));
        var cut = Open(); cut.Find(".npm-link-candidate").Click();
        PickVendor(cut, "Existing");
        Assert.Contains("Verification reference", cut.Find("label[for=npm-confirmation]").TextContent);
        Assert.True(cut.Find("#npm-confirmation").HasAttribute("required"));
        Assert.True(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        cut.Find("#npm-confirmation").Change("Verified office evidence");
        cut.Find("footer .btn-primary").Click();
        cut.WaitForAssertion(() => Assert.NotNull(sent)); Assert.Equal(_source.ContractId, sent!.ContractId); Assert.Equal(_source.StallId, sent.StallId);
        _api.Verify(x => x.RegisterAsync(It.IsAny<RegisterFishMeatVendorRequest>()), Times.Never);
    }
    [Fact]
    public void Linked_creation_uses_original_registration_handler_and_preserves_retry_identity()
    {
        var requests = new List<RegisterFishMeatVendorRequest>();
        _api.Setup(x => x.RegisterAsync(It.IsAny<RegisterFishMeatVendorRequest>())).Callback<RegisterFishMeatVendorRequest>(requests.Add)
            .ReturnsAsync(Result<FishMeatVendorRegistrationDto>.Failure("test", ResultStatus.Invalid));
        var cut = Open(); cut.Find(".npm-link-candidate").Click();
        Assert.Empty(cut.FindAll("#npm-registration-evidence"));
        cut.Find("form").Submit(); cut.Find("form").Submit();
        Assert.Equal(2, requests.Count); Assert.Equal(requests[0].ClientOperationId, requests[1].ClientOperationId);
        Assert.Contains(_source.ContractId.ToString(), requests[0].NpmAssociation!.ConfirmationNote);
        Assert.Contains(_source.StallId.ToString(), requests[0].NpmAssociation!.ConfirmationNote);
        Assert.DoesNotContain("checked", requests[0].NpmAssociation!.ConfirmationNote, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(requests[0].ClientOperationId, requests[0].NpmAssociation!.ClientOperationId);
        Assert.Equal(_source.ContractId, requests[0].NpmAssociation!.ContractId);
    }
    [Fact]
    public void Name_matches_require_review_and_do_not_auto_link()
    {
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1, [_source with { PossibleMatches = [_vendor] }])));
        var cut = Open(); cut.Find(".npm-link-candidate").Click();
        // A similar name starts the review at linking, as a suggestion only: nothing is chosen and nothing can be confirmed until the Head picks one.
        Assert.Contains("Similar names are suggestions only", cut.Markup); Assert.Empty(cut.FindAll(".npm-link-pick.is-picked"));
        Assert.Equal("Search registrations", cut.Find("label[for=npm-vendor-search]").TextContent);
        cut.Find("#npm-confirmation").Change("Confirmed"); Assert.True(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        SwitchPath(cut); Assert.Empty(cut.FindAll("#npm-confirmation"));
        Assert.True(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        Export("npm-review", cut.Markup);
        _api.Verify(x => x.LinkNpmVendorAsync(It.IsAny<Guid>(), It.IsAny<ConfirmNpmVendorAssociationRequest>()), Times.Never);
    }
    [Fact]
    public void Admin_can_review_but_cannot_link_create_or_correct()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        var cut = Open(); cut.Find(".npm-link-candidate").Click();
        Assert.Empty(cut.FindAll("#npm-confirmation")); Assert.Empty(cut.FindAll("footer .btn-primary")); Assert.Empty(cut.FindAll(".npm-link-switch"));
    }
    [Fact]
    public void Loading_failure_is_not_an_empty_verified_registry()
    {
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<NpmVendorCandidates>.Failure("unavailable", ResultStatus.Invalid));
        var cut = Open(); Assert.Contains("could not be loaded", cut.Find("[role=alert]").TextContent); Assert.DoesNotContain("No occupancies match", cut.Markup);
    }
    [Fact]
    public void Exact_contract_deep_link_opens_registration_directly_for_unambiguous_occupant()
    {
        var cut = RenderComponent<NpmVendorRegistrationWorkflow>(p => p.Add(x => x.Show, true)
            .Add(x => x.TaxYear, Year).Add(x => x.InitialContractId, _source.ContractId));
        var form = cut.FindComponent<VendorRegistrationDrawer>();
        Assert.True(form.Instance.Show);
        Assert.Equal(_source.Occupant, form.Instance.Model.Name);
        Assert.Equal(FishMeatVendorType.Fish, form.Instance.Model.Type);
        Assert.Contains(_source.ContractId.ToString(), form.Markup);
        Assert.Empty(cut.FindAll(".npm-link-summary"));
        Assert.Empty(cut.FindAll("#npm-confirmation"));
    }

    [Fact]
    public void Deep_link_with_possible_duplicate_keeps_explicit_identity_review()
    {
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1,
                [_source with { PossibleMatches = [_vendor] }])));
        var cut = RenderComponent<NpmVendorRegistrationWorkflow>(p => p.Add(x => x.Show, true)
            .Add(x => x.TaxYear, Year).Add(x => x.InitialContractId, _source.ContractId));
        Assert.Empty(cut.FindComponents<VendorRegistrationDrawer>());
        Assert.Contains("Similar names are suggestions only", cut.Markup);
        _api.Verify(x => x.RegisterAsync(It.IsAny<RegisterFishMeatVendorRequest>()), Times.Never);
    }
    [Fact]
    public void Candidate_list_is_compact_and_technical_identity_is_disclosed_only_after_selection()
    {
        var cut = Open();
        Assert.DoesNotContain(_source.ContractId.ToString(), cut.Find(".npm-link-candidate").TextContent);
        Assert.Contains("Fish Area · Stall 3", cut.Find(".npm-link-candidate").TextContent);
        Assert.Equal("Not linked", cut.Find(".npm-link-state").TextContent);
        Export("npm-candidates", cut.Markup);
        cut.Find(".npm-link-candidate").Click();
        var form = cut.FindComponent<VendorRegistrationDrawer>();
        Assert.True(form.Instance.Show);
        Assert.Contains(_source.ContractId.ToString(), form.Markup);
        Assert.Empty(cut.FindAll(".npm-link-summary"));
        Assert.Empty(cut.FindAll("#npm-vendor-search"));
        Assert.Contains($"Tax year {Year}", cut.Markup);
        Export("npm-selected", cut.Markup);
    }
    [Fact]
    public void Only_the_chosen_path_renders_controls_and_one_primary_footer_action()
    {
        // A possible existing registration retains the explicit review path.
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1, [_source with { PossibleMatches = [_vendor] }])));
        var cut = Open(); cut.Find(".npm-link-candidate").Click();
        Assert.Single(cut.FindAll("#npm-vendor-search")); Assert.Equal("Confirm link", cut.Find("footer .btn-primary").TextContent);
        Assert.True(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        PickVendor(cut, "Exi");
        Assert.Contains("Fish shop", cut.Find(".npm-link-confirmation").TextContent);
        Assert.Contains("Stall 3", cut.Find(".npm-link-confirmation").TextContent);
        Export("npm-link-existing", cut.Markup);
        SwitchPath(cut);
        Assert.Empty(cut.FindAll("#npm-vendor-search")); Assert.Equal("Register from NPM", cut.Find("footer .btn-primary").TextContent);
        Assert.True(cut.Find("footer .btn-primary").HasAttribute("disabled")); // duplicate must be explicitly reviewed
        cut.Find(".npm-link-options input").Change(true);
        Assert.False(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        Assert.Single(cut.FindAll("footer .btn-primary"));
        cut.FindAll("button").Single(x => x.TextContent == "Back").Click();
        Assert.Empty(cut.FindAll("#npm-confirmation"));
    }
    [Fact]
    public void Historical_exception_is_explicit_and_does_not_create_an_automatic_link()
    {
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1, [_source with { Active = false }])));
        var cut = Open(); cut.Find(".npm-link-candidate").Click();
        Assert.Empty(cut.FindAll("#npm-confirmation"));
        Assert.True(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        cut.Find(".npm-link-options input").Change(true);
        Assert.False(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        _api.Verify(x => x.RegisterAsync(It.IsAny<RegisterFishMeatVendorRequest>()), Times.Never);
    }
    [Fact]
    public void Verified_link_and_correction_history_remain_visible_and_success_refreshes_source()
    {
        var link = new NpmVendorAssociationDto(Guid.NewGuid(), _vendor.Id, _source.ContractId, _source.StallId,
            _source.Occupant, "head", DateTime.UtcNow, "Office checked", null, null, null);
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 0, [_source with { Links = [link] }])));
        _api.Setup(x => x.CorrectNpmVendorLinkAsync(link.Id, It.IsAny<CorrectNpmVendorAssociationRequest>())).ReturnsAsync(Result<NpmVendorAssociationDto>.Success(link with { CorrectedAtUtc = DateTime.UtcNow, CorrectionReason = "Wrong source", CorrectedBy = "head" }));
        var cut = Open(); cut.Find(".npm-link-candidate").Click();
        Assert.Single(cut.FindAll("a").Where(x => x.TextContent == "View registration")); Assert.Empty(cut.FindAll("footer .btn-primary"));
        cut.FindAll("button").Single(x => x.TextContent == "Correct link").Click();
        Assert.True(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        _api.Verify(x => x.CorrectNpmVendorLinkAsync(It.IsAny<Guid>(), It.IsAny<CorrectNpmVendorAssociationRequest>()), Times.Never);
        cut.Find("#npm-correction").Change("Wrong source");
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1, [_source with { Links = [link with { CorrectedAtUtc = DateTime.UtcNow, CorrectionReason = "Wrong source", CorrectedBy = "head" }] }])));
        cut.Find("footer .btn-primary").Click();
        cut.WaitForAssertion(() => Assert.Contains("Corrected link", cut.Markup)); Assert.Contains("Wrong source", cut.Markup);
        Assert.Empty(cut.FindAll("#npm-correction"));
        _api.Verify(x => x.RegisterAsync(It.IsAny<RegisterFishMeatVendorRequest>()), Times.Never);
    }
    [Fact]
    public void Long_identity_is_kept_in_summary_and_evidence_not_in_form_labels()
    {
        var name = new string('A', 180);
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1, [_source with { Occupant = name, PossibleMatches = [_vendor] }])));
        var cut = Open(); cut.Find(".npm-link-candidate").Click();
        Assert.Equal(name, cut.Find(".npm-link-summary h3").TextContent);
        Assert.DoesNotContain(_source.ContractId.ToString(), cut.Find("label[for=npm-vendor-search]").TextContent);
        Export("npm-long-name", cut.Markup);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Linked_context_preserves_original_occupant_and_does_not_open_replacement_profile(bool active)
    {
        var link = new NpmVendorAssociationDto(Guid.NewGuid(), _vendor.Id, _source.ContractId, _source.StallId,
            "Original liable occupant", "head", DateTime.UtcNow, "Office confirmation", null, null, null);
        _api.Setup(x => x.NpmVendorLinksAsync(_vendor.Id)).ReturnsAsync(Result<IReadOnlyList<NpmVendorAssociationDto>>.Success([link]));
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, true, true)).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 0, [_source with { Active = active }])));
        var cut = RenderComponent<NpmVendorLinkedContext>(p => p.Add(x => x.VendorId, _vendor.Id).Add(x => x.TaxYear, Year));
        Assert.Contains("Original liable occupant", cut.Markup); Assert.Contains(_source.ContractId.ToString(), cut.Markup);
        Assert.Equal(active ? 1 : 0, cut.FindAll("a").Count(x => x.TextContent == "Open NPM Account"));
        Assert.Contains($"npmContractId={_source.ContractId}", cut.Markup);
    }
    [Fact]
    public void Npm_profile_entry_carries_verified_contract_and_stall_scope()
    {
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, false, true)).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 1, [_source])));
        var cut = RenderComponent<NpmOccupancyVendorContext>(p => p.Add(x => x.StallId, _source.StallId).Add(x => x.TaxYear, Year));
        var link = cut.Find("a"); Assert.Equal($"/operations/fish-meat-vendor-fees?taxYear={Year}&npmContractId={_source.ContractId}", link.GetAttribute("href"));
    }
    [Fact]
    public void Fish_and_Meat_filter_narrows_the_occupant_rows_and_each_row_carries_its_link_action()
    {
        var meat = _source with { ContractId = Guid.NewGuid(), Occupant = "Mark Lorgadar", Area = "Meat Area", StallNo = "1", SuggestedType = FishMeatVendorType.Meat };
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 2, [_source, meat])));
        var cut = Open();
        Assert.Equal(2, cut.FindAll(".npm-link-candidate").Count);
        cut.FindAll(".npm-link-seg button").Single(x => x.TextContent == "Meat").Click();
        var only = Assert.Single(cut.FindAll(".npm-link-candidate"));
        Assert.Contains("Mark Lorgadar", only.TextContent); Assert.Contains("Meat Area · Stall 1", only.TextContent);
        Assert.Equal("Link or register this vendor", only.GetAttribute("title"));
        Assert.Equal("true", cut.FindAll(".npm-link-seg button").Single(x => x.TextContent == "Meat").GetAttribute("aria-pressed"));
        cut.FindAll(".npm-link-seg button").Single(x => x.TextContent == "Fish").Click();
        Assert.Contains("Verified occupant", Assert.Single(cut.FindAll(".npm-link-candidate")).TextContent);
    }
    [Fact]
    public void An_already_linked_occupant_shows_its_link_and_offers_nothing_until_a_separate_registration_is_asked_for()
    {
        var link = new NpmVendorAssociationDto(Guid.NewGuid(), _vendor.Id, _source.ContractId, _source.StallId,
            _source.Occupant, "head", DateTime.UtcNow, "Office checked", null, null, null);
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(Year, 0, [_source with { Links = [link] }])));
        var cut = Open();
        Assert.Equal("Linked", cut.Find(".npm-link-state").TextContent);
        cut.Find(".npm-link-candidate").Click();
        Assert.Empty(cut.FindAll("#npm-confirmation")); Assert.Empty(cut.FindAll("footer .btn-primary"));
        Assert.Contains(_vendor.DisplayName, cut.Find("[aria-label='Registration status']").TextContent);
        Assert.Equal("Verified", cut.Find(".npm-link-record-heading .npm-link-state").TextContent);
        Assert.Equal($"/operations/fish-meat-vendor-fees/manage?taxYear={Year}&registrationId={_vendor.Id}", cut.Find(".npm-link-actions a").GetAttribute("href"));
        Assert.All(cut.FindAll(".npm-link-evidence"), details => Assert.False(details.HasAttribute("open")));
        Assert.False(cut.Find(".npm-link-secondary").HasAttribute("open"));
        Assert.DoesNotContain("This occupant is already linked.", cut.Markup);
        cut.FindAll("button").Single(x => x.TextContent == "Link a separate registration").Click();
        Assert.Single(cut.FindAll("#npm-vendor-search")); Assert.Single(cut.FindAll("#npm-confirmation"));
        SwitchPath(cut);
        Assert.True(cut.Find("footer .btn-primary").HasAttribute("disabled"));
        cut.Find(".npm-link-options input").Change(true);
        cut.Find("footer .btn-primary").Click();
        Assert.Empty(cut.FindAll("#npm-registration-evidence"));
        Assert.False(cut.Find("button[type=submit]").HasAttribute("disabled"));
        _api.Verify(x => x.RegisterAsync(It.IsAny<RegisterFishMeatVendorRequest>()), Times.Never);
    }
    [Fact]
    public void AdminCanViewLinkedRegistration_ButCannotCorrectOrAddASeparateLink()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        var link = new NpmVendorAssociationDto(Guid.NewGuid(), _vendor.Id, _source.ContractId, _source.StallId,
            _source.Occupant, "head", DateTime.UtcNow, "Office checked", null, null, null);
        _api.Setup(x => x.NpmVendorCandidatesAsync(Year, It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(
            Result<NpmVendorCandidates>.Success(new(Year, 0, [_source with { Links = [link] }])));
        var cut = Open();
        cut.Find(".npm-link-candidate").Click();
        Assert.Single(cut.FindAll(".npm-link-actions a"));
        Assert.Empty(cut.FindAll(".npm-link-actions button, .npm-link-secondary, footer .btn-primary"));
    }
    private static void Export(string name, string markup)
    {
        var directory = Environment.GetEnvironmentVariable("STALLTRACK_NPM_LINK_FIXTURE_DIR"); if (string.IsNullOrEmpty(directory)) return;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var links = new[] { "EEMOCantilanSDS.Client/wwwroot/css/site.min.css", "EEMOCantilanSDS.Client/wwwroot/app.css", "EEMOCantilanSDS.Client/obj/Release/net10.0/scopedcss/bundle/EEMOCantilanSDS.Client.styles.css" }
            .Select(p => $"<link rel='stylesheet' href='{new Uri(Path.Combine(root, p)).AbsoluteUri}'>");
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name + ".html"), "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>" + string.Join("", links) + "</head><body>" + markup + "</body></html>");
    }
}
