using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TpmPage = EEMOCantilanSDS.Client.Components.Pages.Menus.Facilities.TPM;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>The Tabo operation must show posted SRC collections without inventing legacy attendance.</summary>
public sealed class TpmCanonicalCollectionsPageTests : TestContext
{
    private static readonly DateOnly Day = PhilippineTime.Today;
    private readonly Mock<ITpmApiClient> _tpm = new();
    private readonly Mock<ICollectionActivityApiClient> _activity = new();

    private static CollectionActivityEventDto Entry(string code, string name, decimal amount, string src) =>
        new(src, "Canonical", "FeeScheduleCollection", Guid.NewGuid(), Day,
            DateTime.UtcNow, src, null, RevenueInstrumentType.OfficialReceipt, [], [], name,
            null, null, "Bobby Lugo", "Bobby Lugo", FacilityCode.TPM, name, null,
            amount, 0m, amount, "Posted", null,
            [new CollectionActivityLineDto(code, code, amount, null, null, null, null, null, null)], []);

    private IRenderedComponent<TpmPage> Open(params CollectionActivityEventDto[] events)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var first = new DateOnly(Day.Year, Day.Month, 1);
        _tpm.Setup(x => x.GetOverviewAsync(Day.Year, Day.Month))
            .ReturnsAsync(Result<TpmOverviewDto>.Success(new TpmOverviewDto
            {
                MarketDates = [Day], CollectedThisMonth = 0m, VendorEntriesThisMonth = 0
            }));
        _tpm.Setup(x => x.GetMarketDaysAsync(Day.Year, Day.Month))
            .ReturnsAsync(Result<IReadOnlyList<TpmMarketDayDto>>.Success([]));
        _tpm.Setup(x => x.GetMonthAttendanceAsync(Day.Year, Day.Month))
            .ReturnsAsync(Result<IReadOnlyList<TpmVendorAttendanceDto>>.Success([]));
        _tpm.Setup(x => x.GetVendorAttendanceAsync(Day))
            .ReturnsAsync(Result<IReadOnlyList<TpmVendorAttendanceDto>>.Success([]));
        _activity.Setup(x => x.GetAsync(first, first.AddMonths(1).AddDays(-1),
                null, null, "Canonical", 500, null))
            .ReturnsAsync(Result<CollectionActivityFeedDto>.Success(new(
                first, first.AddMonths(1).AddDays(-1), "BusinessDate", "LatestCorrected",
                DateTime.UtcNow, events, events.Length, false, 0m,
                events.Sum(e => e.Amount), 0m, events.Sum(e => e.NetAmount), [])));

        Services.AddSingleton(_tpm.Object);
        Services.AddSingleton(_activity.Object);
        Services.AddSingleton(new FacilityState(Mock.Of<IFacilitiesApiClient>()));
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IOfficeSourcesApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton<BrandingState>();
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");

        return RenderComponent<TpmPage>();
    }

    [Fact]
    public void Source_activity_displays_only_Tabo_SRC_on_market_day_and_does_not_create_legacy_attendance()
    {
        var cut = Open(Entry(RevenueClassificationCodes.Tabo, "Philers Richard", 100m, "SRC-2026-000031"),
            Entry("ECF", "Unrelated", 25m, "SRC-2026-000032"));
        cut.WaitForAssertion(() => Assert.Contains("₱100.00", cut.Find(".facility-hero-stats").TextContent));
        cut.Find(".tpm-friday-item").Click();
        cut.WaitForAssertion(() => Assert.Contains("SRC-2026-000031", cut.Find("table[aria-label='SRC-backed Tabo entries']").TextContent));
        Assert.Contains("Philers Richard", cut.Markup);
        Assert.DoesNotContain("SRC-2026-000032", cut.Markup);
        Assert.Empty(cut.FindAll(".tpm-vendor-table:not([aria-label]) tbody tr"));
        _tpm.Verify(x => x.GetVendorAttendanceAsync(Day), Times.Once);
    }
}
