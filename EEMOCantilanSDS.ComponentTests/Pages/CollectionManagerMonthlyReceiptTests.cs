using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using CollectionExceptions = EEMOCantilanSDS.Client.Components.Pages.Reports.CollectionExceptions;

/// <summary>
/// Collection Manager, monthly rental facilities: a Legacy month keeps the status editor; a Canonical month offers one
/// Record payment entry (the existing payment dialog) and never the status editor; a month mid-cutover offers nothing.
/// </summary>
public sealed class CollectionManagerMonthlyReceiptTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Guid StallId = Guid.NewGuid();
    private readonly Mock<IPaymentsApiClient> _payments = new();

    private void Arrange(SettlementAuthority authority)
    {
        var today = PhilippineTime.Today;
        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(x => x.GetNpmRatesAsync()).ReturnsAsync(Result<NpmRatesDto>.Success(new(30m, 1m)));
        stalls.Setup(x => x.GetStallsByFacilityPaginatedAsync(FacilityCode.NCC, It.IsAny<MarketSection?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(Result<CursorPagedResult<StallDto>>.Success(new()
            {
                Items = [new(StallId, "PREVIEW-NCC-01", StallStatus.Active, "Synthetic Occupant", "Synthetic Occupant", 4.0, DateTime.Today.AddYears(-1), 1200m, null, null,
                    null, null, null, null, 3)],
            }));
        _payments.Setup(x => x.GetMonthlyExceptionsAsync(StallId, today.Year)).ReturnsAsync(Result<IReadOnlyList<int>>.Success([]));
        _payments.Setup(x => x.GetPaymentRecordAsync(StallId, today.Year, It.IsAny<int>()))
            .ReturnsAsync((Guid _, int _, int month) => month == today.Month
                ? Result<PaymentRecordDto>.Success(new(Guid.NewGuid(), PaymentStatus.Unpaid, null, 1200m, null, null, null, 0m, 1200m, authority))
                : Result<PaymentRecordDto>.Failure("none"));
        _payments.Setup(x => x.GetOutstandingMonthsAsync(StallId, It.IsAny<Guid?>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .ReturnsAsync(Result<IReadOnlyList<PaymentHistoryDto>>.Success([
                new($"{today.Year}-{today.Month:00}", PaymentStatus.Unpaid, 1200, 0, 1200, null, null, SettlementAuthority: authority)]));
        var facilities = new Mock<IFacilitiesApiClient>();
        facilities.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
            {
                new(FacilityCode.NCC, "New Commercial Center", "NCC", 0, null, null, null),
            }));
        var catalog = new EEMOCantilanSDS.Client.Services.FacilityState(facilities.Object);
        catalog.EnsureLoadedAsync().GetAwaiter().GetResult();

        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(stalls.Object);
        Services.AddSingleton(_payments.Object);
        Services.AddSingleton(Mock.Of<IDailyCollectionApiClient>());
        var rent = new Mock<IEcfCollectionsApiClient>();
        rent.Setup(x => x.GetRentObligationAsync(StallId, today.Year, today.Month)).ReturnsAsync(Result<EEMOCantilanSDS.Application.Dtos.Revenue.RentObligationQuoteDto>.Success(
            new(Guid.NewGuid(), Guid.NewGuid(), CollectionSourceKind.PaymentRecord, StallId, "PREVIEW-NCC-01", "NCC", today.Year, today.Month,
                Guid.NewGuid(), null, "Synthetic Occupant", 1200, 0, 1200, authority, 4, Guid.NewGuid(), Guid.NewGuid(), "Rent",
                RevenueInstrumentType.OfficialReceipt, 1200, true, true, false, true)));
        Services.AddSingleton(rent.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IObligationsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(catalog);
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    private IRenderedComponent<CollectionExceptions> OpenCurrentMonth()
    {
        var cut = RenderComponent<CollectionExceptions>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ce-stall")), Timeout);
        cut.Find(".ce-stall").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ce-month")), Timeout);
        cut.FindAll(".ce-month")[PhilippineTime.Today.Month - 1].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ce-drawer")), Timeout);
        return cut;
    }

    [Fact]
    public void A_canonical_month_offers_Record_payment_and_not_the_status_editor()
    {
        Arrange(SettlementAuthority.Canonical);
        var cut = OpenCurrentMonth();

        Assert.Empty(cut.FindAll(".ce-set-opts"));
        Assert.Empty(cut.FindAll(".ce-or-input"));
        var button = Assert.Single(cut.FindAll(".ce-receipt button"));
        Assert.Equal("Record payment", button.TextContent.Trim());
        Assert.False(button.HasAttribute("disabled"));
        _payments.Verify(x => x.RecordPaymentAsync(It.IsAny<RecordPaymentCommand>()), Times.Never);

        button.Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".pb-modal")), Timeout); // the existing payment dialog
        _payments.Verify(x => x.RecordPaymentAsync(It.IsAny<RecordPaymentCommand>()), Times.Never);
    }

    [Fact]
    public void A_legacy_month_keeps_the_status_editor_unchanged()
    {
        Arrange(SettlementAuthority.Legacy);
        var cut = OpenCurrentMonth();

        Assert.NotEmpty(cut.FindAll(".ce-set-opts"));
        Assert.Empty(cut.FindAll(".ce-receipt"));
        Assert.Empty(cut.FindAll(".pb-modal"));
    }

    [Fact]
    public void A_month_mid_cutover_stays_fail_closed_with_no_editor_and_no_payment_entry()
    {
        Arrange(SettlementAuthority.PendingCutover);
        var cut = OpenCurrentMonth();

        Assert.Empty(cut.FindAll(".ce-set-opts"));
        Assert.Empty(cut.FindAll(".ce-receipt button"));
        Assert.Contains("cannot be edited or paid", cut.Find(".ce-receipt").TextContent);
        Assert.Empty(cut.FindAll(".pb-modal"));
    }
}
