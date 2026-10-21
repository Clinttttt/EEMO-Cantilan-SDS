using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Import history lists the bulk imports that were actually saved for one space-rental line: who, when, how many rows were imported and how many
/// were skipped. It states failure with a retry and an honest empty state, and asks only for the line its route names.
/// </summary>
public sealed class SpaceHolderImportHistoryTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly Mock<IObligationsApiClient> _api = new();

    public SpaceHolderImportHistoryTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Head").SetRoles("SuperAdmin");
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>()); Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>()); Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<BrandingState>();
    }

    private IRenderedComponent<SpaceHolderImportHistory> Open(string slug)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/operations/{slug}/import/history");
        return RenderComponent<SpaceHolderImportHistory>();
    }

    [Fact]
    public void SavedImportsAreListedWithWhoWhenAndTheCountsOfWhatWasImportedAndSkipped()
    {
        _api.Setup(x => x.GetImportRunsAsync(ObligationKind.KanmanggaySpaceRental)).ReturnsAsync(Result<IReadOnlyList<SpaceHolderImportRunDto>>.Success(
        [
            new(Guid.NewGuid(), ObligationKind.KanmanggaySpaceRental, new DateTime(2026, 10, 12, 2, 30, 0, DateTimeKind.Utc), "Head Reviewer", 12, 10, 2, "Row 3: This space or event lot is already recorded.")
        ]));

        var cut = Open("kanmanggay");

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tbody tr"));
            Assert.Contains("Oct 12, 2026", row.TextContent);
            Assert.Contains("10:30 AM", row.TextContent);                                                  // shown in Philippine time
            Assert.Contains("Head Reviewer", row.TextContent);
            Assert.Equal(new[] { "12", "10", "2" }, row.QuerySelectorAll("td.v3-num").Select(x => x.TextContent.Trim()).ToArray());
            Assert.Contains("already recorded", row.TextContent);
            Assert.Equal("1 saved import", cut.Find(".v3-panel-meta").TextContent.Trim());
            Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href") == "/operations/kanmanggay/import");
        }, Timeout);
    }

    [Fact]
    public void AnOperationWithNoSavedImportSaysSoPlainly()
    {
        _api.Setup(x => x.GetImportRunsAsync(ObligationKind.VegetableFruitSpaceRental)).ReturnsAsync(Result<IReadOnlyList<SpaceHolderImportRunDto>>.Success([]));

        var cut = Open("vegetable-fruit");

        cut.WaitForAssertion(() => Assert.Contains("No list has been imported into Vegetable / Fruits yet.", cut.Markup), Timeout);
        _api.Verify(x => x.GetImportRunsAsync(ObligationKind.KanmanggaySpaceRental), Times.Never);       // only the line its route names
    }

    [Fact]
    public void AFailedReadIsStatedWithARetry_NeverShownAsNoImports()
    {
        _api.SetupSequence(x => x.GetImportRunsAsync(ObligationKind.FiestaArawLotRental))
            .ReturnsAsync(Result<IReadOnlyList<SpaceHolderImportRunDto>>.Failure("Unavailable"))
            .ReturnsAsync(Result<IReadOnlyList<SpaceHolderImportRunDto>>.Success([]));

        var cut = Open("fiesta-araw");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unavailable", cut.Find("[role=alert]").TextContent);
            Assert.DoesNotContain("No list has been imported", cut.Markup);
        }, Timeout);
        cut.Find("[role=alert] button").Click();
        cut.WaitForAssertion(() => Assert.Contains("No list has been imported into Fiesta / Araw yet.", cut.Markup), Timeout);
    }
}
