using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>ST-20 activation is governed by the server, never inferred from a Head UI checkbox.</summary>
public sealed class MonthlyRentSrcActivationPageTests : TestContext
{
    private readonly Mock<IFacilitiesApiClient> _facilities = new();
    private readonly Mock<IRentActivationsApiClient> _activation = new();
    private readonly Mock<ICollectorsApiClient> _collectors = new();

    public MonthlyRentSrcActivationPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _facilities.Setup(x => x.GetFacilityConfigurationAsync())
            .ReturnsAsync(Result<FacilityConfigurationDto>.Success(new(
                [new ConfiguredFacilityDto("NCC", "New Commercial Center", "NCC", null, "Monthly rental", true, 2, [])],
                [])));
        _collectors.Setup(x => x.GetAllCollectorsAsync())
            .ReturnsAsync(Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.CollectorListDto>>.Success([]));
        Services.AddSingleton(_facilities.Object);
        Services.AddSingleton(_activation.Object);
        Services.AddSingleton(_collectors.Object);
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
    }

    private IRenderedComponent<MonthlyRentSrcActivation> RenderPage()
    {
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        return RenderComponent<MonthlyRentSrcActivation>();
    }

    [Fact]
    public void Restricted_to_authorized_heads_and_administrators()
    {
        var authorize = Assert.Single(typeof(MonthlyRentSrcActivation)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("SuperAdmin,Admin", authorize.Roles);
    }

    [Fact]
    public void Opening_page_does_not_write_or_activate_without_review()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.Contains("New Commercial Center", page.Markup));
        Assert.Contains("Existing payments are never converted", page.Markup);
        Assert.DoesNotContain("Activate reviewed unpaid rows", page.Markup);
        _activation.Verify(x => x.ActivateAsync(It.IsAny<RentActivationRequest>()), Times.Never);
    }

    [Fact]
    public void Paid_legacy_rows_are_excluded_and_cannot_be_activated()
    {
        var legacyRow = new RentActivationRowDto(Guid.NewGuid(), "27", "Shane Lopa",
            Guid.NewGuid(), SettlementAuthority.Legacy, false, "Existing legacy settlement evidence", []);
        _activation.Setup(x => x.PlanAsync(It.IsAny<RentActivationPlanRequest>()))
            .ReturnsAsync(Result<RentActivationPlanDto>.Success(new(
                FacilityCode.NCC, 2026, 10, [legacyRow], 0, 1, [], false, "legacy-fingerprint", [])));

        var page = RenderPage();
        page.WaitForElement("button", TimeSpan.FromSeconds(5));
        page.FindAll("button").Single(b => b.TextContent.Contains("Review existing")).Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Shane Lopa", page.Markup);
            Assert.Contains("Existing legacy settlement evidence", page.Markup);
            Assert.Contains("No fresh unpaid rows", page.Markup);
            Assert.DoesNotContain("Activate reviewed unpaid rows", page.Markup);
        });
        _activation.Verify(x => x.ActivateAsync(It.IsAny<RentActivationRequest>()), Times.Never);
    }

    [Fact]
    public void Fresh_unpaid_rows_require_evidence_and_server_approval_before_activation()
    {
        var id = Guid.NewGuid();
        var unpaid = new RentActivationRowDto(Guid.NewGuid(), "28", "New occupant",
            null, null, true, null, ["Collector device evidence not yet verified"]);
        _activation.Setup(x => x.PlanAsync(It.IsAny<RentActivationPlanRequest>()))
            .ReturnsAsync(Result<RentActivationPlanDto>.Success(new(
                FacilityCode.NCC, 2026, 10, [unpaid], 1, 0, [], false, "not-ready", [id])));

        var page = RenderPage();
        page.WaitForAssertion(() => Assert.Contains("Review existing", page.Markup));
        page.FindAll("button").Single(b => b.TextContent.Contains("Review existing")).Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("New occupant", page.Markup);
            Assert.Contains("Collector device evidence not yet verified", page.Markup);
            Assert.Contains("Not approved for activation", page.Markup);
            Assert.True(page.FindAll("button").Single(b => b.TextContent.Contains("Activate reviewed")).HasAttribute("disabled"));
        });
        _activation.Verify(x => x.ActivateAsync(It.IsAny<RentActivationRequest>()), Times.Never);
    }

    [Fact]
    public void Changing_billing_scope_clears_prior_attestations_instead_of_reusing_device_or_backup_evidence()
    {
        var row = new RentActivationRowDto(Guid.NewGuid(), "28", "Unpaid occupant", null, null, true, null, []);
        _activation.Setup(x => x.PlanAsync(It.IsAny<RentActivationPlanRequest>()))
            .ReturnsAsync(Result<RentActivationPlanDto>.Success(new(
                FacilityCode.NCC, 2026, 10, [row], 1, 0, [], false, "not-ready", [])));

        var page = RenderPage();
        page.WaitForAssertion(() => Assert.Contains("Review existing", page.Markup));
        page.FindAll("button").Single(b => b.TextContent.Contains("Review existing")).Click();
        page.WaitForElement(".srca-checks input[type=checkbox]");
        page.Find(".srca-checks input[type=checkbox]").Change(true);
        page.Find("input[maxlength='200']").Change("signed-checklist-A");
        Assert.True(page.Find(".srca-checks input[type=checkbox]").HasAttribute("checked"));

        page.Find("select[aria-label='Billing month']").Change("9");
        page.FindAll("button").Single(b => b.TextContent.Contains("Review existing")).Click();
        page.WaitForElement(".srca-checks input[type=checkbox]");
        Assert.False(page.Find(".srca-checks input[type=checkbox]").HasAttribute("checked"));
        Assert.Equal("", page.Find("input[maxlength='200']").GetAttribute("value"));
        _activation.Verify(x => x.ActivateAsync(It.IsAny<RentActivationRequest>()), Times.Never);
    }
}
