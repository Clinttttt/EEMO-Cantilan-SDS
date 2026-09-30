using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Facility assignments and non-facility operation assignments (WCF, Landing/Berthing, …) are separate permissions.
/// An operation-only collector is allowed and is never given a placeholder facility; an edit that could not read the
/// collector's operations leaves them untouched instead of clearing them.
/// </summary>
public sealed class CollectorOperationAssignmentsTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly Guid CollectorId = Guid.NewGuid();

    public CollectorOperationAssignmentsTests()
    {
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(FacilityCatalogFixture.WithNoRecord());
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Create_OperationOnlyCollector_SendsOperationCodes_WithNoFacility()
    {
        var api = CollectorsApi();
        CreateCollectorCommand? sent = null;
        api.Setup(x => x.CreateCollectorAsync(It.IsAny<CreateCollectorCommand>()))
            .Callback<CreateCollectorCommand>(c => sent = c)
            .ReturnsAsync(Result<CollectorDto>.Success(new CollectorDto(
                Guid.NewGuid(), "Ana Reyes", "C-002", "ana", "", "", true, [])));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<Collector>();
        cut.WaitForAssertion(() => Assert.Contains("Ana Cruz", cut.Markup), Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Contains("Add Collector")).Click();
        cut.WaitForAssertion(() => Assert.Contains("Assigned Operations", cut.Markup), Timeout);
        FillRequiredFields(cut, password: "secret-123");

        OperationCheckbox(cut, "Landing / Berthing").Change(true);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Create Account").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Empty(sent!.AssignedFacilities);
            Assert.Equal([CollectorOperationCodes.LandingBerthing], sent.OperationCodes);
        }, Timeout);
    }

    [Fact]
    public void Create_WithNeitherFacilityNorOperation_IsRefused()
    {
        var api = CollectorsApi();
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<Collector>();
        cut.WaitForAssertion(() => Assert.Contains("Ana Cruz", cut.Markup), Timeout);

        cut.FindAll("button").Single(b => b.TextContent.Contains("Add Collector")).Click();
        FillRequiredFields(cut, password: "secret-123");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Create Account").Click();

        Assert.Contains("Assign at least one facility or operation.", cut.Markup);
        api.Verify(x => x.CreateCollectorAsync(It.IsAny<CreateCollectorCommand>()), Times.Never);
    }

    [Fact]
    public void Edit_LoadsServerOperations_AndReplacesThemOnSave()
    {
        var api = CollectorsApi();
        api.Setup(x => x.GetCollectionOperationsAsync(CollectorId)).ReturnsAsync(
            Result<IReadOnlyList<CollectorOperationAssignmentDto>>.Success(new[]
            {
                new CollectorOperationAssignmentDto(CollectorOperationCodes.Wcf, "Water Consumption Fee", true),
                new CollectorOperationAssignmentDto(CollectorOperationCodes.MarketFees, "Market Fees", false),
            }));
        UpdateCollectorCommand? sent = null;
        api.Setup(x => x.UpdateCollectorAsync(It.IsAny<UpdateCollectorCommand>()))
            .Callback<UpdateCollectorCommand>(c => sent = c)
            .ReturnsAsync(Result<bool>.Success(true));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<Collector>();
        cut.WaitForAssertion(() => Assert.Contains("Ana Cruz", cut.Markup), Timeout);
        cut.Find("button[title='Edit']").Click();

        cut.WaitForAssertion(() => Assert.True(OperationCheckbox(cut, "Water Consumption Fee").HasAttribute("checked")), Timeout);
        OperationCheckbox(cut, "Market Fees").Change(true);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save Changes").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Equal([FacilityCode.NPM], sent!.AssignedFacilities);
            Assert.Equal(
                new[] { CollectorOperationCodes.Wcf, CollectorOperationCodes.MarketFees }.Order(),
                sent.OperationCodes!.Order());
        }, Timeout);
    }

    [Fact]
    public void Edit_WhenOperationsCannotBeRead_LeavesThemUntouched()
    {
        var api = CollectorsApi();
        api.Setup(x => x.GetCollectionOperationsAsync(CollectorId)).ReturnsAsync(
            Result<IReadOnlyList<CollectorOperationAssignmentDto>>.Failure("boom"));
        UpdateCollectorCommand? sent = null;
        api.Setup(x => x.UpdateCollectorAsync(It.IsAny<UpdateCollectorCommand>()))
            .Callback<UpdateCollectorCommand>(c => sent = c)
            .ReturnsAsync(Result<bool>.Success(true));
        Services.AddSingleton(api.Object);

        var cut = RenderComponent<Collector>();
        cut.WaitForAssertion(() => Assert.Contains("Ana Cruz", cut.Markup), Timeout);
        cut.Find("button[title='Edit']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Operation assignments could not be loaded", cut.Markup), Timeout);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save Changes").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(sent);
            Assert.Null(sent!.OperationCodes);
        }, Timeout);
    }

    private static AngleSharp.Dom.IElement OperationCheckbox(IRenderedComponent<Collector> cut, string name) =>
        cut.FindAll(".op-group label").Single(l => l.TextContent.Contains(name)).QuerySelector("input")!;

    private static void FillRequiredFields(IRenderedComponent<Collector> cut, string password)
    {
        var drawer = cut.Find(".eemo-drawer-footer").ParentElement!;
        var inputs = drawer.QuerySelectorAll("input.form-input");
        inputs[0].Input("Ana Reyes");
        cut.Find(".eemo-drawer-footer").ParentElement!.QuerySelectorAll("input.form-input")
            .Single(i => i.GetAttribute("placeholder") == "Login username").Input("ana");
        cut.Find(".eemo-drawer-footer").ParentElement!.QuerySelectorAll("input.form-input")
            .Single(i => i.GetAttribute("placeholder") == "Set initial password").Input(password);
    }

    private static Mock<ICollectorsApiClient> CollectorsApi()
    {
        var api = new Mock<ICollectorsApiClient>();
        api.Setup(x => x.GetAllCollectorsAsync()).ReturnsAsync(Result<IReadOnlyList<CollectorListDto>>.Success(new[]
        {
            new CollectorListDto(CollectorId, "Ana Cruz", "ana@example.test", "C-001", [FacilityCode.NPM], 0m, 0, null, true),
        }));
        api.Setup(x => x.GetNextEmployeeIdAsync()).ReturnsAsync(Result<string>.Success("C-002"));
        api.Setup(x => x.GetCollectorByIdAsync(CollectorId)).ReturnsAsync(Result<CollectorActivityDto>.Success(
            new CollectorActivityDto(CollectorId, "Ana Cruz", "C-001", "ana@example.test", "", [FacilityCode.NPM],
                0m, 0, 1, null, [], "ana")));
        return api;
    }
}
