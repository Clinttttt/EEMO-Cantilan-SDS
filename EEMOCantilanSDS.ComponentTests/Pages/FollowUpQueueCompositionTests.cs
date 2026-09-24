using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using FollowUpPage = EEMOCantilanSDS.Client.Components.Pages.Reports.FollowUpQueue;

public sealed class FollowUpQueueCompositionTests : TestContext
{
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void LoadsExistingQueueApiAndShowsReasonSeparatelyFromPriority()
    {
        var (cut, api) = RenderQueue();

        cut.WaitForAssertion(() =>
        {
            var lowerAge = RowFor(cut, "Lower-age delinquent");
            Assert.Equal("Delinquent", lowerAge.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.Contains("Priority", lowerAge.QuerySelector(".fq-priority")?.TextContent);
            Assert.Contains("Normal", lowerAge.QuerySelector(".fq-priority")?.TextContent);
            Assert.Contains("Unpaid · 2 months", lowerAge.TextContent);

            var higherAge = RowFor(cut, "Higher-age delinquent");
            Assert.Equal("Delinquent", higherAge.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.Contains("Critical", higherAge.QuerySelector(".fq-priority")?.TextContent);
            Assert.Contains("Unpaid · 4 months", higherAge.TextContent);
        }, RenderTimeout);

        api.Verify(a => a.GetFollowUpQueueAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void KeepsCurrentPeriodUnpaidAndPartialSeparateFromDelinquency()
    {
        var (cut, _) = RenderQueue();

        cut.WaitForAssertion(() =>
        {
            var unpaid = RowFor(cut, "Current unpaid");
            Assert.Equal("Current-period unpaid", unpaid.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.Contains("Unpaid", unpaid.TextContent);
            Assert.DoesNotContain("Delinquent", unpaid.TextContent);
            Assert.Contains("September 2026", unpaid.TextContent);

            var partial = RowFor(cut, "Current partial");
            Assert.Equal("Partial payment", partial.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.Contains("Partial", partial.TextContent);
            Assert.DoesNotContain("Delinquent", partial.TextContent);
        }, RenderTimeout);
    }

    [Fact]
    public void KeepsSourceReasonWhenStatusMentionsOccupantAndKeepsExpiryFreeOfDebtLanguage()
    {
        var (cut, _) = RenderQueue();

        cut.WaitForAssertion(() =>
        {
            var lapsedTerm = RowFor(cut, "Lapsed-term occupant");
            Assert.Equal("Past occupancy balance", lapsedTerm.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.DoesNotContain("Ended occupancy balance", lapsedTerm.TextContent);
            Assert.Contains("No longer the occupant", lapsedTerm.TextContent);
            Assert.Contains("Jan–Aug 2026", lapsedTerm.TextContent);
            Assert.DoesNotContain("Delinquent", lapsedTerm.TextContent);
            Assert.False(lapsedTerm.TextContent.Contains("Arrears", StringComparison.OrdinalIgnoreCase));

            var expiring = RowFor(cut, "Expiring account");
            Assert.Equal("Contract expiring", expiring.QuerySelector(".fq-reason")?.TextContent.Trim());
            Assert.Contains("Expiring soon", expiring.TextContent);
            Assert.DoesNotContain("Delinquent", expiring.TextContent);
            Assert.DoesNotContain("₱", expiring.TextContent);
        }, RenderTimeout);
    }

    [Fact]
    public void PreservesInlineActionsAndDoesNotInferArrears()
    {
        var (cut, _) = RenderQueue();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Record payment", StringComparison.Ordinal));
            Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Add OR", StringComparison.Ordinal));
            Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Pay Bill", StringComparison.Ordinal));
            Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Hide locally", StringComparison.Ordinal));
            Assert.Contains(cut.FindAll("a"), link => link.TextContent.Contains("Review contract", StringComparison.Ordinal));
            Assert.Contains("Past-period view", cut.Markup);
            Assert.False(cut.Markup.Contains("Arrears", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("not recorded as follow-up history", cut.Markup);
        }, RenderTimeout);
    }

    private (IRenderedComponent<FollowUpPage> Cut, Mock<IReportsApiClient> Api) RenderQueue()
    {
        var reportsApi = new Mock<IReportsApiClient>();
        reportsApi.Setup(api => api.GetFollowUpQueueAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<FollowUpQueueDto>.Success(SampleQueue()));
        Services.AddSingleton(reportsApi.Object);

        var facilitiesApi = new Mock<IFacilitiesApiClient>();
        facilitiesApi.Setup(api => api.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Failure("Unavailable", 500));
        Services.AddSingleton<IFacilitiesApiClient>(facilitiesApi.Object);
        Services.AddSingleton(new FacilityState(facilitiesApi.Object));

        var municipalitiesApi = new Mock<IMunicipalitiesApiClient>();
        municipalitiesApi.Setup(api => api.GetCurrentBrandingAsync())
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("Unavailable", 500));
        Services.AddSingleton(new BrandingState(municipalitiesApi.Object));

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>());
        Services.AddSingleton(Mock.Of<ITrmApiClient>());
        Services.AddSingleton(Mock.Of<ITpmApiClient>());
        Services.AddSingleton(Mock.Of<ISlaughterApiClient>());
        this.AddTestAuthorization().SetAuthorized("SuperAdmin");

        return (RenderComponent<FollowUpPage>(), reportsApi);
    }

    private static FollowUpQueueDto SampleQueue() => new(
        "September 2026",
        new DateOnly(2026, 9, 24),
        new FollowUpItemDto[]
        {
            Item(2, "Normal", "Delinquent", "delinquent", "Lower-age delinquent", "TCC · Stall 04", 4_800m,
                "12 months to August 2026", "Unpaid · 2 months", "View vendor", "/profile/tcc/00000000-0000-0000-0000-000000000001"),
            Item(1, "Critical", "Delinquent", "delinquent", "Higher-age delinquent", "TCC · Stall 08", 9_600m,
                "12 months to August 2026", "Unpaid · 4 months", "View vendor", "/profile/tcc/00000000-0000-0000-0000-000000000002"),
            Item(2, "Normal", "Current-period unpaid", "current", "Current unpaid", "TCC · Stall 10", 1_200m,
                "September 2026", "Unpaid", "View vendor", "/profile/tcc/00000000-0000-0000-0000-000000000003"),
            Item(2, "Normal", "Partial payment", "current", "Current partial", "NCC · Stall 11", 600m,
                "September 2026", "Partial", "View vendor", "/profile/ncc/00000000-0000-0000-0000-000000000004"),
            Item(1, "High", "Past occupancy balance", "delinquent", "Lapsed-term occupant", "TCC · Stall 12", 3_200m,
                "Jan–Aug 2026", "No longer the occupant", "Review account", "/profile/tcc/00000000-0000-0000-0000-000000000005"),
            Item(2, "Normal", "Contract expiring", "contract", "Expiring account", "BBQ · Stall 2", null,
                "December 31, 2026", "Expiring soon", "Review contract", "/profile/bbq/00000000-0000-0000-0000-000000000006"),
            Item(1, "High", "Missing OR", "missingor", "Receipt follow-up", "TCC · Stall 13", 800m,
                "September 2026", "Paid · OR blank", "Add OR", "/profile/tcc/00000000-0000-0000-0000-000000000007"),
            Item(2, "Normal", "Electricity unpaid", "misc", "Utility follow-up", "NPM · Stall 14", 400m,
                "September 2026", "Unpaid", "Pay Bill", "/npm")
        });

    private static FollowUpItemDto Item(
        int section,
        string priority,
        string reason,
        string reasonKind,
        string person,
        string identifier,
        decimal? amount,
        string period,
        string status,
        string action,
        string link) => new(
            section, priority, reason, reasonKind, FacilityCode.TCC, "Monthly rental", person, identifier,
            amount, false, period, status, action, link,
            StallId: Guid.NewGuid());

    private static IElement RowFor(IRenderedComponent<FollowUpPage> cut, string person) =>
        cut.FindAll(".fq-row").Single(row => row.TextContent.Contains(person, StringComparison.Ordinal));
}
