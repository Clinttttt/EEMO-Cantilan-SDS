using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TransactionsPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Transactions;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class CollectionActivityPageTests : TestContext
{
    private readonly Mock<ITransactionsApiClient> _transactions = new();

    private IRenderedComponent<TransactionsPage> RenderPage()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        _transactions
            .Setup(client => client.GetRecentAsync(
                It.IsAny<FacilityCode?>(), It.IsAny<DateOnly?>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<TransactionFeedDto>>.Success(
                Array.Empty<TransactionFeedDto>()));

        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(client => client.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(
                Array.Empty<FacilitySidebarSummaryDto>()));

        Services.AddSingleton(_transactions.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<BrandingState>();
        this.AddTestAuthorization().SetAuthorized("Admin");

        return RenderComponent<TransactionsPage>();
    }

    [Fact]
    public void RendersCollectionActivityIdentityWithoutAnAuditTrailLinkOrCollectionDateClaim()
    {
        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Collection Activity", page.Find("h1.facility-hero-title").TextContent.Trim());
            Assert.Equal("Collection Activity", page.Find(".topbar-title").TextContent.Trim());
            var emptyState = page.Find(".txn-empty").TextContent;
            Assert.Contains("No collection activity", emptyState, StringComparison.Ordinal);
            Assert.DoesNotContain("No transactions", emptyState, StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(5));

        var markup = page.Markup.ToLowerInvariant();
        Assert.DoesNotContain("/audit-trail", markup);
        Assert.DoesNotContain("collection date", markup);
    }

    [Fact]
    public void KeepsTheTransactionsClientAndFacilityAndDayFilters()
    {
        var page = RenderPage();
        var selectedDay = DateOnly.Parse(page.Find("input[type='date']").GetAttribute("value")!);

        page.WaitForAssertion(
            () => _transactions.Verify(client => client.GetRecentAsync(null, selectedDay, 200), Times.Once),
            TimeSpan.FromSeconds(5));

        page.FindAll("button").Single(button => button.TextContent.Trim() == "NPM").Click();
        page.WaitForAssertion(
            () => _transactions.Verify(client => client.GetRecentAsync(FacilityCode.NPM, selectedDay, 200), Times.Once),
            TimeSpan.FromSeconds(5));

        page.Find("button[aria-label='Previous day']").Click();
        page.WaitForAssertion(
            () => _transactions.Verify(client => client.GetRecentAsync(
                FacilityCode.NPM, selectedDay.AddDays(-1), 200), Times.Once),
            TimeSpan.FromSeconds(5));
    }
}
