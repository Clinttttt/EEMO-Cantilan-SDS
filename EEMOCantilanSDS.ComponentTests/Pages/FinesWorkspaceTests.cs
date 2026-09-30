using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Fines / Penalties (IA-049): the Head defines approved penalties, a fine reaches an Official Receipt only from one of
/// them in Current Collection, and this workspace reads the posted register. It never records money or offers a
/// free-text amount.
/// </summary>
public sealed class FinesWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IPenaltiesApiClient> _api = new();

    public FinesWorkspaceTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(_api.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    private void Serve(IReadOnlyList<PenaltyDefinitionDto>? definitions = null, IReadOnlyList<PenaltyRegisterRowDto>? register = null)
    {
        _api.Setup(x => x.GetDefinitionsAsync())
            .ReturnsAsync(Result<IReadOnlyList<PenaltyDefinitionDto>>.Success(definitions ?? []));
        _api.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<PenaltyRegisterRowDto>>.Success(register ?? []));
    }

    private static PenaltyDefinitionDto Late(decimal amount = 50m) => new(
        Guid.NewGuid(), "LATE_PAYMENT", "Late payment penalty", "Stall rent arrears", GovernedServiceBasis.FixedAmount,
        amount, null, true, new DateOnly(2026, 1, 1));

    [Fact]
    public void Route_IsOfficeOnly_AndTheWorkspaceRecordsNothing()
    {
        Assert.Equal("/operations/fines", Assert.Single(typeof(Fines).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Equal("SuperAdmin,Admin", Assert.Single(typeof(Fines).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Serve();

        var cut = RenderComponent<Fines>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Fines", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            Assert.Empty(cut.FindAll("main"));
            Assert.Contains("No penalty has been approved yet", cut.Markup);
            Assert.Contains("No fines were collected", cut.Markup);
            Assert.DoesNotContain("aren't recorded in StallTrack yet", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains("Record") || b.TextContent.Contains("Collect"));
            // No free-text charge anywhere until the Head opens the definition form.
            Assert.Empty(cut.FindAll("input[type='number']"));
        }, Timeout);
    }

    [Fact]
    public void ShowsApprovedPenaltiesAndThePostedRegisterWithTheOrTheyRodeOn()
    {
        Serve([Late(), new(Guid.NewGuid(), "ILLEGAL_VENDING", "Illegal vending", null, GovernedServiceBasis.DirectApprovedAmount, null, 200m, false, new DateOnly(2026, 3, 1))],
            [new(Guid.NewGuid(), new DateOnly(2026, 9, 30), DateTime.UtcNow, "Lisa Reyes", "Stall 4 · August rent",
                "LATE_PAYMENT", "Late payment penalty", "OR-000125", 50m, "Posted"),
             new(Guid.NewGuid(), new DateOnly(2026, 9, 29), DateTime.UtcNow, "Ben Cruz", null,
                "LATE_PAYMENT", "Late payment penalty", "OR-000120", 50m, "Reversed")]);

        var cut = RenderComponent<Fines>();

        cut.WaitForAssertion(() =>
        {
            var defs = cut.FindAll("[aria-label='Approved penalties'] tbody tr");
            Assert.Equal(2, defs.Count);
            Assert.Contains("Fixed · ₱50.00", defs[0].TextContent);
            Assert.Contains("Manually approved · up to ₱200.00", defs[1].TextContent);
            Assert.Contains("Retired", defs[1].TextContent);

            var rows = cut.FindAll("[aria-label='Fines collected'] tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.Contains("Lisa Reyes", rows[0].TextContent);
            Assert.Contains("Stall 4 · August rent", rows[0].TextContent);
            Assert.Contains("OR-000125", rows[0].TextContent);
            // A reversed fine is listed but not counted in the total.
            Assert.Contains("₱50.00", cut.Find("tfoot").TextContent);
            Assert.DoesNotContain("₱100.00", cut.Find("tfoot").TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheHeadDefinesAPenalty_AsAnApprovedVersion_NotAFreeTextCharge()
    {
        Serve();
        DefinePenaltyRequest? sent = null;
        _api.Setup(x => x.DefineAsync(It.IsAny<DefinePenaltyRequest>()))
            .Callback<DefinePenaltyRequest>(r => sent = r)
            .ReturnsAsync(Result<PenaltyDefinitionDto>.Success(Late(75m)));

        var cut = RenderComponent<Fines>();
        cut.WaitForAssertion(() => cut.Find("button.v3-btn-sm"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Define penalty").Click();
        cut.Find("form[aria-label='Define a penalty'] input").Change("LATE_PAYMENT");
        cut.FindAll("form[aria-label='Define a penalty'] input")[1].Change("Late payment penalty");
        cut.Find("form[aria-label='Define a penalty'] input[type='number']").Change("75");
        cut.Find("form[aria-label='Define a penalty']").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal("LATE_PAYMENT", sent!.Code);
            Assert.Equal(GovernedServiceBasis.FixedAmount, sent.Basis);
            Assert.Equal(75m, sent.FixedAmount);
            Assert.Null(sent.MaximumAmount);
            Assert.Contains("Penalty saved", cut.Markup);
        }, Timeout);
    }

    [Fact]
    public void AnAdmin_CanReadPenaltiesAndTheRegister_ButCannotDefineOne()
    {
        var context = this.AddTestAuthorization();
        context.SetAuthorized("admin").SetRoles("Admin");
        Serve([Late()]);

        var cut = RenderComponent<Fines>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Late payment penalty", cut.Markup);
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Define penalty");
        }, Timeout);
    }

    [Fact]
    public void FailedLoads_SayTheyCouldNotBeLoaded_InsteadOfSuggestingNothingIsApproved()
    {
        _api.Setup(x => x.GetDefinitionsAsync()).ReturnsAsync(Result<IReadOnlyList<PenaltyDefinitionDto>>.Failure("offline"));
        _api.Setup(x => x.GetRegisterAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<PenaltyRegisterRowDto>>.Failure("offline"));

        var cut = RenderComponent<Fines>();

        cut.WaitForAssertion(() =>
        {
            var alerts = cut.FindAll("[role='alert']").Select(x => x.TextContent).ToList();
            Assert.Contains(alerts, a => a.Contains("Approved penalties couldn't be loaded"));
            Assert.Contains(alerts, a => a.Contains("Fines couldn't be loaded"));
            Assert.DoesNotContain("No penalty has been approved yet", cut.Markup);
        }, Timeout);
    }
}
