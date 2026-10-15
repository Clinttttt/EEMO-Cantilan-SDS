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
            "F-12", null, "Office reference", DateTime.UtcNow, NpmRegistrationRequestStatus.Submitted, 1, null, null, null, null, null);
        _api.Setup(x => x.NpmRequestsAsync(1, 25)).ReturnsAsync(Result<NpmRegistrationRequestPage>.Success(new([row], 1, 1, 25)));
        _api.Setup(x => x.NpmVendorCandidatesAsync(row.TaxYear, true, true)).ReturnsAsync(Result<NpmVendorCandidates>.Success(new(row.TaxYear, 0, [])));
        ReviewNpmRegistrationRequest? sent = null;
        _api.Setup(x => x.ReviewNpmRequestAsync(row.Id, It.IsAny<ReviewNpmRegistrationRequest>()))
            .Callback<Guid, ReviewNpmRegistrationRequest>((_, r) => sent = r).ReturnsAsync(Result<NpmRegistrationRequestDto>.Failure("Retry"));
        var view = RenderComponent<EEMOCantilanSDS.Client.Components.Pages.Menus.PendingNpmRequests>();
        view.FindAll("button").Single(x => x.TextContent == "View details").Click();
        Assert.Contains("No cash received", view.Markup);
        Assert.Equal("", view.FindComponents<FhSelect>().Single().Instance.Value);
        Assert.DoesNotContain("_association", view.Markup);
        Assert.True(view.FindAll("button").Single(x => x.TextContent == "Approve registration").HasAttribute("disabled"));
        view.Find("#pnr-reason").Input("Provide tenancy evidence");
        view.FindAll("button").Single(x => x.TextContent == "Return for correction").Click(); Assert.Null(sent);
        view.FindAll("button").Single(x => x.TextContent == "Confirm decision").Click();
        view.WaitForAssertion(() => Assert.NotNull(sent));
        Assert.Equal(NpmRegistrationRequestStatus.Returned, sent!.Outcome); Assert.Equal(1, sent.ExpectedVersion); Assert.Null(sent.AssociationId);
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
