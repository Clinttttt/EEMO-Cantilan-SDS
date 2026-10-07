using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Transportation / Parking (IA-030, IA-050) is a Cash Ticket collection at the approved rate of a vehicle class. The Head
/// sets the classes and rates; the workspace never records money and never offers a typed amount.
/// </summary>
public sealed class TransportationWorkspaceTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IVehicleClassesApiClient> _classes = new();
    private readonly Mock<IGovernedServicesApiClient> _governed = new();

    public TransportationWorkspaceTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(_classes.Object);
        Services.AddSingleton(_governed.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        _governed.Setup(x => x.GetActivityAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(Result<IReadOnlyList<GovernedServiceActivityDto>>.Success([]));
        _governed.Setup(x => x.GetDefinitionsAsync()).ReturnsAsync(Result<IReadOnlyList<GovernedServiceDefinitionDto>>.Success(new[]
        {
            new GovernedServiceDefinitionDto(CollectorOperationCodes.Transportation, "Transportation / Parking",
                RevenueClassificationCodes.TransportationParking, false, [GovernedServiceBasis.VehicleClassRate],
                GovernedServiceSetupState.Active, GovernedServiceBasis.VehicleClassRate, null, null, false,
                new DateOnly(2026, 9, 1), [new(null, RevenueInstrumentType.CashTicket, "Transportation")], [])
        }));
    }

    private static VehicleClassDto Jeepney(decimal? amount = 20m) => new(
        Guid.NewGuid(), "JEEPNEY", "Jeepney", true, amount, amount is null ? null : new DateOnly(2026, 1, 1));

    private void Serve(params VehicleClassDto[] classes) =>
        _classes.Setup(x => x.GetAsync()).ReturnsAsync(Result<IReadOnlyList<VehicleClassDto>>.Success(classes));

    [Fact]
    public void TransportationIsADirectAmountCashTicketPage_WithNoVehicleClassRateOrCeiling()
    {
        Assert.Equal("/operations/transportation",
            Assert.Single(typeof(Transportation).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template);
        Assert.Equal("SuperAdmin,Admin",
            Assert.Single(typeof(Transportation).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        _governed.Setup(x => x.GetTransportationCurrentAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(
            Result<TransportationCurrentActivityDto>.Success(new(PhilippineTime.Today, PhilippineTime.Today, PhilippineTime.Today, 150m, 2, 6, 150m, 2,
                [new(Guid.NewGuid(), PhilippineTime.Today, DateTime.UtcNow, "SRC-2026-000031", RevenueInstrumentType.CashTicket, null, "Parking", null, 150m, "Ana", "Posted")])));
        Serve(Jeepney());

        var cut = RenderComponent<Transportation>();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Transportation / Parking", Assert.Single(cut.FindAll("h1")).TextContent.Trim());
            Assert.Contains("Cash Ticket", cut.Find(".v3h").TextContent);
            Assert.Contains("SRC-2026-000031", cut.Markup);
            Assert.Contains("₱150.00", cut.Find("tfoot").TextContent);
            foreach (var word in new[] { "Vehicle class", "vehicle class", "Change amount rule", "Ceiling", "ceiling", "TRM", "transporter", "Rate" })
                Assert.DoesNotContain(word, cut.Markup);                                           // the old class / rate setup is not on this page
            Assert.Empty(cut.FindAll("form"));                                                       // nothing here records money
        }, Timeout);
    }

    [Fact]
    public void TheHeadCanOnlySwitchTransportationOnForCollectors_WithADirectAmountAndNoRule()
    {
        _governed.Setup(x => x.GetTransportationCurrentAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>())).ReturnsAsync(
            Result<TransportationCurrentActivityDto>.Success(new(PhilippineTime.Today, PhilippineTime.Today, PhilippineTime.Today, 0m, 0, 0, 0m, 0, [])));
        ConfigureGovernedServiceRequest? sent = null;
        _governed.Setup(x => x.ConfigureAsync(CollectorOperationCodes.Transportation, It.IsAny<ConfigureGovernedServiceRequest>()))
            .Callback<string, ConfigureGovernedServiceRequest>((_, r) => sent = r)
            .ReturnsAsync(Result<GovernedServiceDefinitionDto>.Success(new GovernedServiceDefinitionDto(CollectorOperationCodes.Transportation, "Transportation / Parking",
                RevenueClassificationCodes.TransportationParking, false, [GovernedServiceBasis.DirectApprovedAmount], GovernedServiceSetupState.Active,
                GovernedServiceBasis.DirectApprovedAmount, null, null, true, PhilippineTime.Today, [], [])));

        var cut = RenderComponent<Transportation>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "Enable for collectors"), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Enable for collectors").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal((GovernedServiceBasis.DirectApprovedAmount, (decimal?)null, (decimal?)null, true, true),
                (sent!.Basis, sent.FixedAmount, sent.MaximumAmount, sent.IsEnabled, sent.MobileEnabled));
            Assert.Contains("Enabled", cut.Find(".tp-status").TextContent);
        }, Timeout);
    }

    [Fact]
    public void ListsTheClassesWithTheApprovedRateInForce_AndAClassWithoutOneIsSaidToHaveNone()
    {
        Serve(Jeepney(20m), new VehicleClassDto(Guid.NewGuid(), "VAN", "Van", true, null, null));

        var cut = RenderComponent<VehicleClassRatesHost>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("[aria-label='Vehicle classes'] tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.Contains("₱20.00", rows[0].TextContent);
            Assert.Contains("No rate in force", rows[1].TextContent);
        }, Timeout);
    }

    [Fact]
    public void TheHeadDefinesAClassAndItsRate_AsAnEffectiveDatedVersion()
    {
        Serve();
        SaveVehicleClassRequest? sent = null;
        _classes.Setup(x => x.SaveAsync(It.IsAny<SaveVehicleClassRequest>()))
            .Callback<SaveVehicleClassRequest>(r => sent = r)
            .ReturnsAsync(Result<VehicleClassDto>.Success(Jeepney()));

        var cut = RenderComponent<VehicleClassRatesHost>();
        cut.WaitForAssertion(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add class"), Timeout);
        Assert.Empty(cut.FindAll("form"));                                        // the form is not left under the table
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "+ Add class").Click();
        cut.WaitForAssertion(() => cut.Find("form[aria-label='Define a vehicle class rate']"), Timeout);
        var form = "form[aria-label='Define a vehicle class rate']";
        cut.FindAll($"{form} input[type='text']")[0].Change("jeepney");
        cut.FindAll($"{form} input[type='text']")[1].Change("Jeepney");
        cut.Find($"{form} input[type='number']").Change("20");
        cut.Find(form).Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal(("jeepney", "Jeepney", 20m), (sent!.Code, sent.DisplayName, sent.Amount));
        }, Timeout);
    }

    [Fact]
    public void AnAdmin_ReadsTheRates_ButCannotDefineOrChangeThem()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("Admin");
        Serve(Jeepney());

        var cut = RenderComponent<VehicleClassRatesHost>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Jeepney", cut.Markup);
            Assert.Empty(cut.FindAll("form"));
            Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() is "Change rate" or "Retire" or "Add class");
        }, Timeout);
    }

    [Fact]
    public void FailedLoad_SaysSo_InsteadOfSuggestingNoClassExists()
    {
        _classes.Setup(x => x.GetAsync()).ReturnsAsync(Result<IReadOnlyList<VehicleClassDto>>.Failure("offline"));

        var cut = RenderComponent<VehicleClassRatesHost>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("couldn't be loaded", cut.Find("[role='alert']").TextContent);
            Assert.DoesNotContain("has been defined yet", cut.Markup);
        }, Timeout);
    }
}

/// <summary>Hosts the panel on its own so its behaviour is tested apart from the page chrome.</summary>
public sealed class VehicleClassRatesHost : ComponentBase
{
    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        builder.OpenComponent<EEMOCantilanSDS.Client.Components.Shared.VehicleClassRates>(0);
        builder.CloseComponent();
    }
}
