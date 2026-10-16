using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class RegisteredFishMeatPickerTests : TestContext
{
    private readonly Mock<IOfficeSourcesApiClient> _api = new();
    public RegisteredFishMeatPickerTests()
    {
        Services.AddSingleton(_api.Object); JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }
    [Fact]
    public void Picker_selects_exact_identity_and_keeps_closed_registration_disabled()
    {
        var year = PhilippineTime.Today.Year;
        var first = new FishMeatVendorRegistrationDto(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Same name", "Fish shop", "East", null);
        var second = first with { Id = Guid.NewGuid(), BusinessName = "Closed shop" };
        VendorRegistrationManagementRow Row(FishMeatVendorRegistrationDto v, VendorRegistrationStatus status) => new(v, status, "Head", DateTime.UtcNow, null, null, null, null, null, 0m, 0m);
        _api.Setup(x => x.ManageRegistrationsAsync(year, It.IsAny<int>(), null, null)).ReturnsAsync(Result<VendorRegistryManagement>.Success(new(year, year, 10,
            [Row(first, VendorRegistrationStatus.Active), Row(second, VendorRegistrationStatus.Closed)], 0m)));
        _api.Setup(x => x.NpmVendorCandidatesAsync(year, true, true)).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(year, 0, [])));
        FishMeatVendorRegistrationDto? selected = null;
        var view = RenderComponent<RegisteredFishMeatPicker>(p => p.Add(x => x.Show, true).Add(x => x.OnSelected, v => selected = v));
        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll(".rfm-row").Count));
        Assert.Contains("absent link does not prove", view.Markup);
        Assert.True(view.FindAll(".rfm-row")[1].QuerySelector("button")!.HasAttribute("disabled"));
        view.FindAll(".rfm-row")[0].QuerySelector("button")!.Click(); Assert.Equal(first.Id, selected!.Id);
        view.Find("#rfm-search").Input("Closed shop"); Assert.Single(view.FindAll(".rfm-row"));
        Assert.DoesNotContain("Fish shop", view.Find(".rfm-row").TextContent);
    }

    [Fact]
    public void Request_link_opens_exact_fish_meat_vendor_for_head_registration()
    {
        var year = PhilippineTime.Today.Year;
        var first = new FishMeatVendorRegistrationDto(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Identical name", "Shop A", "East", null);
        var second = first with { Id = Guid.NewGuid(), BusinessName = "Shop B" };
        VendorRegistrationManagementRow Row(FishMeatVendorRegistrationDto v) => new(v, VendorRegistrationStatus.Active, "Head", DateTime.UtcNow, null, null, null, null, null, 0m, 0m);
        _api.Setup(x => x.ManageRegistrationsAsync(year, It.IsAny<int>(), null, null))
            .ReturnsAsync(Result<VendorRegistryManagement>.Success(new(year, year, 10, [Row(first), Row(second)], 0m)));
        _api.Setup(x => x.NpmVendorCandidatesAsync(year, true, true))
            .ReturnsAsync(Result<NpmVendorCandidates>.Success(new(year, 0, [])));
        FishMeatVendorRegistrationDto? picked = null;
        var view = RenderComponent<RegisteredFishMeatPicker>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.PreferredVendorId, second.Id)
            .Add(x => x.PreferredYear, year)
            .Add(x => x.OnSelected, v => picked = v));
        view.WaitForAssertion(() => Assert.Single(view.FindAll(".rfm-row")));
        Assert.Contains("Shop B", view.Find(".rfm-row").TextContent);
        Assert.DoesNotContain("Shop A", view.Find(".rfm-row").TextContent);
        Assert.True(view.FindComponent<VendorDrawerShell>().Instance.AnimateEntry);
        view.Find(".rfm-row button:last-child").Click();
        Assert.Equal(second.Id, picked?.Id);
        view.Find(".rfm-preferred button").Click();
        Assert.Equal(2, view.FindAll(".rfm-row").Count);
    }

    [Fact]
    public void Office_details_editor_updates_exact_existing_registration_without_creating_another()
    {
        var current = new FishMeatVendorRegistrationDto(Guid.NewGuid(), PhilippineTime.Today.Year,
            FishMeatVendorType.Fish, VendorRegistrationKind.New, "Josephine Reyes",
            "reyes fishland", "San Vicent Carmen SurigaoSan Vicent Carmen Surigao", null);
        var row = new VendorRegistrationManagementRow(current, VendorRegistrationStatus.Active,
            "Head", DateTime.UtcNow, null, null, null, null, null, 0m, 0m);
        CorrectVendorOfficeDetailsRequest? requested = null;
        _api.Setup(x => x.CorrectOfficeDetailsAsync(current.Id, It.IsAny<CorrectVendorOfficeDetailsRequest>()))
            .Callback<Guid, CorrectVendorOfficeDetailsRequest>((_, r) => requested = r)
            .ReturnsAsync(Result<VendorRegistrationMutationResult>.Failure("try again"));
        var cut = RenderComponent<VendorOfficeDetailsEditor>(p => p.Add(x => x.Row, row));
        Assert.Contains("Josephine Reyes", cut.Markup);
        cut.Find("#vod-address").Input("San Vicente, Carmen, Surigao del Sur");
        cut.Find("#vod-last").Input("Reyes");
        cut.Find("#vod-nature").Input("Retail sale of fish");
        cut.Find("#vod-form").Submit();
        cut.WaitForAssertion(() => Assert.NotNull(requested));
        Assert.Equal(current, requested!.Before);
        Assert.Equal("Reyes", requested.LastName);
        Assert.Equal("Retail sale of fish", requested.NatureOfBusiness);
        Assert.Equal("San Vicente, Carmen, Surigao del Sur", requested.Address);
        _api.Verify(x => x.RegisterAsync(It.IsAny<RegisterFishMeatVendorRequest>()), Times.Never);
    }

    [Fact]
    public void Doubled_vendor_address_is_presented_once_without_changing_the_saved_record()
    {
        var raw = "San Vicente, CarmenSan Vicente, Carmen";
        Assert.Equal("San Vicente, Carmen", VendorAddressDisplay.Clean(raw));
        Assert.Equal(raw, raw);
        Assert.Equal("Not recorded", VendorAddressDisplay.Clean(null));
        Assert.Equal("San Vicente, Carmen", VendorAddressDisplay.Clean("San Vicente, Carmen"));
    }

    [Fact]
    public void Collector_proposed_Fish_Area_stall_2_is_prefilled_not_replaced_by_next_number()
    {
        var year = PhilippineTime.Today.Year;
        var vendor = new FishMeatVendorRegistrationDto(Guid.NewGuid(), year, FishMeatVendorType.Fish,
            VendorRegistrationKind.New, "Josephine Reyes", "reyes fishland", "Carmen", null);
        var form = new EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal.VendorModalForm
        {
            FacilityCode = "NPM", SelectedSection = "Vegetable", StallNo = "", ContractYears = 1
        };
        Assert.True(NpmRequestStallPrefill.TryApply(form, vendor, vendor.Id, year, " 2 ", "FishSection"));
        Assert.Equal("Fish", form.SelectedSection);
        Assert.Equal("2", form.StallNo);

        var cut = RenderComponent<EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal>(p => p
            .Add(x => x.Show, true).Add(x => x.Form, form)
            .Add(x => x.ExistingStallNos, new[] { "1", "24" }));
        Assert.Equal("2", form.StallNo); // Normal next-number suggestion would be 25.
        cut.SetParametersAndRender(p => p.Add(x => x.ExistingStallNos, new[] { "1", "24", "26" }));
        Assert.Equal("2", form.StallNo); // A rerender may not overwrite the proposed number.
        Assert.False(NpmRequestStallPrefill.TryApply(form, vendor, Guid.NewGuid(), year, "99", "MeatSection"));
        Assert.Equal("2", form.StallNo);
        Assert.Equal("Fish", form.SelectedSection);
        Assert.False(NpmRequestStallPrefill.TryApply(form, vendor, vendor.Id, year - 1, "99", "MeatSection"));
        Assert.False(NpmRequestStallPrefill.TryApply(form, vendor, vendor.Id, year, " ", "MeatSection"));
    }

    [Fact]
    public void Registry_action_is_opt_in_to_new_NPM_registration_and_preserves_entered_form()
    {
        var form = new EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal.VendorModalForm {
            FacilityCode = "NPM", StallNo = "F-12", ActualOccupant = "Already entered", ContractYears = 2 };
        var clicked = false;
        var view = RenderComponent<EEMOCantilanSDS.Client.Components.Pages.Shared.AddVendorModal>(p => p.Add(x => x.Show, true)
            .Add(x => x.Form, form).Add(x => x.OnRegisteredFishMeat, () => clicked = true));
        view.Find("[aria-label='Select a registered Fish or Meat vendor for NPM']").Click();
        Assert.True(clicked); Assert.Equal("F-12", form.StallNo); Assert.Equal(2, form.ContractYears);
        Assert.Equal("Already entered", form.ActualOccupant);
        view.SetParametersAndRender(p => p.Add(x => x.IsEditing, true));
        Assert.Empty(view.FindAll("[aria-label='Select a registered Fish or Meat vendor for NPM']"));
    }

    [Fact]
    public void Request_review_shows_nonfinancial_state_and_return_requires_explicit_confirmation()
    {
        var row = new NpmRegistrationRequestDto(Guid.NewGuid(), Guid.NewGuid(), PhilippineTime.Today.Year, "Verified vendor", Guid.NewGuid(), "Collector",
            "2", EEMOCantilanSDS.Domain.Enums.MarketSection.FishSection, "Office reference", DateTime.UtcNow, NpmRegistrationRequestStatus.Submitted, 1, null, null, null, null, null);
        _api.Setup(x => x.NpmRequestsAsync(1, 25)).ReturnsAsync(Result<NpmRegistrationRequestPage>.Success(new([row], 1, 1, 25)));
        _api.Setup(x => x.NpmVendorCandidatesAsync(row.TaxYear, true, true)).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(row.TaxYear, 0, [])));
        ReviewNpmRegistrationRequest? sent = null;
        _api.Setup(x => x.ReviewNpmRequestAsync(row.Id, It.IsAny<ReviewNpmRegistrationRequest>()))
            .Callback<Guid, ReviewNpmRegistrationRequest>((_, r) => sent = r).ReturnsAsync(Result<NpmRegistrationRequestDto>.Failure("Retry"));
        var view = RenderComponent<EEMOCantilanSDS.Client.Components.Pages.Menus.PendingNpmRequests>();
        view.FindAll("button").Single(x => x.TextContent == "Review").Click();
        Assert.Contains("Pending review", view.Markup);
        Assert.Contains("No cash received", view.Markup);
        Assert.Empty(view.FindComponents<FhSelect>());
        Assert.Contains("No verified NPM occupancy linked", view.Markup);
        Assert.Contains($"/npm?registerVendor={row.VendorRegistrationId:D}&amp;registrationYear={row.TaxYear}&amp;proposedStall=2&amp;proposedSection=FishSection", view.Markup);
        Assert.True(view.FindComponent<VendorDrawerShell>().Instance.AnimateEntry);
        Assert.Contains("Check again", view.Markup);
        Assert.DoesNotContain("_association", view.Markup);
        Assert.True(view.FindAll("button").Single(x => x.TextContent == "Approve registration").HasAttribute("disabled"));
        view.Find("#pnr-reason").Input("Provide tenancy evidence");
        view.FindAll("button").Single(x => x.TextContent == "Return for correction").Click(); Assert.Null(sent);
        view.FindAll("button").Single(x => x.TextContent == "Confirm decision").Click();
        view.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.Equal(NpmRegistrationRequestStatus.Returned, sent!.Outcome); Assert.Equal(1, sent.ExpectedVersion); Assert.Null(sent.AssociationId);
    }

    [Fact]
    public void Verified_NPM_link_shows_selectable_occupancy_and_requires_an_explicit_choice()
    {
        var vendor = Guid.NewGuid();
        var contract = Guid.NewGuid();
        var stall = Guid.NewGuid();
        var row = new NpmRegistrationRequestDto(Guid.NewGuid(), vendor, PhilippineTime.Today.Year, "Verified vendor",
            Guid.NewGuid(), "Collector", "F-12", null, "Office reference", DateTime.UtcNow,
            NpmRegistrationRequestStatus.Submitted, 1, null, null, null, null, null);
        var link = new NpmVendorAssociationDto(Guid.NewGuid(), vendor, contract, stall, "Verified vendor",
            "Head", DateTime.UtcNow, "Verified occupancy", null, null, null);
        var candidate = new NpmVendorCandidate(contract, stall, "F-12", "Verified vendor", "Fish Area",
            true, DateOnly.FromDateTime(DateTime.Today), null, "current", [link], []);
        _api.Setup(x => x.NpmRequestsAsync(1, 25))
            .ReturnsAsync(Result<NpmRegistrationRequestPage>.Success(new([row], 1, 1, 25)));
        _api.Setup(x => x.NpmVendorCandidatesAsync(row.TaxYear, true, true))
            .ReturnsAsync(Result<NpmVendorCandidates>.Success(new(row.TaxYear, 0, [candidate])));

        var view = RenderComponent<EEMOCantilanSDS.Client.Components.Pages.Menus.PendingNpmRequests>();
        view.FindAll("button").Single(x => x.TextContent == "Review").Click();

        var selector = view.FindComponents<FhSelect>().Single();
        Assert.DoesNotContain("No verified NPM occupancy linked", view.Markup);
        Assert.Equal(2, selector.Instance.Options.Count);
        var approval = view.FindAll("button").Single(x => x.TextContent == "Approve registration");
        Assert.True(approval.HasAttribute("disabled"));

        selector.Find(".fh-dd-trigger").Click();
        selector.FindAll(".fh-dd-item").Last().Click();
        view.Find("#pnr-reason").Input("Verified and linked by office");
        approval = view.FindAll("button").Single(x => x.TextContent == "Approve registration");
        Assert.False(approval.HasAttribute("disabled"));
    }

    [Fact]
    public void A_failed_evidence_read_never_offers_unverified_candidates()
    {
        _api.Setup(x => x.ManageRegistrationsAsync(It.IsAny<int>(), It.IsAny<int>(), null, null)).ReturnsAsync(Result<VendorRegistryManagement>.Failure("Offline"));
        _api.Setup(x => x.NpmVendorCandidatesAsync(It.IsAny<int>(), true, true)).ReturnsAsync(Result<NpmVendorCandidates>.Failure("Offline"));
        var view = RenderComponent<RegisteredFishMeatPicker>(p => p.Add(x => x.Show, true));
        view.WaitForAssertion(() => Assert.Contains("Could not verify", view.Find("[role=alert]").TextContent));
        Assert.Empty(view.FindAll(".rfm-row"));
    }
}
