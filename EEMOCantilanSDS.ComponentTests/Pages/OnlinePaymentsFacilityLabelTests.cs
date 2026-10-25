using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// Online Payments → Awaiting OR names each payment's facility by the office's own acronym ("RR"), not by the internal slot code the transaction carries
/// ("Custom1"). The code stays the identity; only the wording comes from the tenant's facility record.
/// </summary>
public sealed class OnlinePaymentsFacilityLabelTests : TestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    public OnlinePaymentsFacilityLabelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("Admin").SetRoles("Admin");
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<BrandingState>();
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
    }

    private IRenderedComponent<OnlinePayments> Open(params OnlinePaymentAwaitingOrDto[] awaiting)
    {
        var online = new Mock<IOnlinePaymentsApiClient>();
        online.Setup(a => a.GetAwaitingOrAsync()).ReturnsAsync(Result<IReadOnlyList<OnlinePaymentAwaitingOrDto>>.Success(awaiting));
        online.Setup(a => a.GetDashboardAsync()).ReturnsAsync(Result<OnlinePaymentDashboardDto>.Failure("not needed here", 500));
        Services.AddSingleton(online.Object);
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(
        [
            new(FacilityCode.Custom1, "Rest Rooms", "RR", 0),
            new(FacilityCode.Custom2, "Cold Storage", "", 0),                      // no acronym saved: derived from the name
            new(FacilityCode.TCC, "Tampak Commercial Center", "TCC", 0)
        ]));
        Services.AddSingleton(facilities.Object);
        Services.AddSingleton(new FacilityState(facilities.Object));
        return RenderComponent<OnlinePayments>();
    }

    private static OnlinePaymentAwaitingOrDto Item(FacilityCode facility, string stall) =>
        new(Guid.NewGuid(), $"EEMO-OP-{stall}", facility, stall, "Angelica Reyes", "October 2026", 1m, "qrph", new DateTime(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void AwaitingOrNamesEachFacilityByTheOfficesOwnAcronym_NeverItsInternalSlotCode()
    {
        var cut = Open(Item(FacilityCode.Custom1, "2"), Item(FacilityCode.Custom2, "5"), Item(FacilityCode.TCC, "9"));

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr").Select(r => r.TextContent).ToList();
            Assert.Contains(rows, r => r.Contains("RR") && r.Contains("Angelica Reyes"));
            Assert.Contains(rows, r => r.Contains("CS"));                           // derived from "Cold Storage"
            Assert.Contains(rows, r => r.Contains("TCC"));
            Assert.DoesNotContain("Custom1", cut.Markup);
            Assert.DoesNotContain("Custom2", cut.Markup);
        }, Wait);
    }
}
