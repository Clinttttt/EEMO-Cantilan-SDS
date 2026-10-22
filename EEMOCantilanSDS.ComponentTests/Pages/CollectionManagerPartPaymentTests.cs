using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

using CollectionExceptions = EEMOCantilanSDS.Client.Components.Pages.Reports.CollectionExceptions;

/// <summary>
/// Recording an NPM payment by amount on the Collection Manager: when the server accepts the amount as full or part installments the
/// office sees the server's own allocation preview (never one computed here) and records exactly that amount; when part payments are not
/// available the existing whole-day behaviour stays.
/// </summary>
public sealed class CollectionManagerPartPaymentTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Guid StallId = Guid.NewGuid();
    private readonly Mock<IDailyCollectionApiClient> _daily = new();

    private static NpmPartPaymentQuoteDto Quote(string outcome, decimal amount, decimal applied, params NpmPartPaymentLineDto[] lines) =>
        new(StallId, DateTime.Today.Year, DateTime.Today.Month, DateOnly.FromDateTime(DateTime.Today), outcome,
            outcome == "Applied" ? null : "That is more than the month owes.", amount, applied, 900m, 900m - applied, 0m, amount - applied,
            lines, "token-1", RevenueInstrumentType.OfficialReceipt);

    public CollectionManagerPartPaymentTests()
    {
        var stalls = new Mock<IStallsApiClient>();
        stalls.Setup(x => x.GetNpmRatesAsync()).ReturnsAsync(Result<NpmRatesDto>.Success(new(30m, 1m)));
        stalls.Setup(x => x.GetStallsByFacilityPaginatedAsync(FacilityCode.NPM, It.IsAny<MarketSection?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(Result<CursorPagedResult<StallDto>>.Success(new()
            {
                Items = [new(StallId, "V-01", StallStatus.Active, "Ana Vendor", "Ana Vendor", 4.0, DateTime.Today.AddYears(-1), 900m, 30m, null,
                    MarketSection.VegetableArea, null, null, null, 3)],
            }));
        _daily.Setup(x => x.GetDailyCollectionMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<DailyCollectionMonthDto>.Success(new(DateTime.Today.Year, DateTime.Today.Month, 30, 0, 30, 0m, 0m, 0m, 0m, false, [])));
        _daily.Setup(x => x.QuoteNpmAmountAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>()))
            .ReturnsAsync(Result<NpmAmountQuoteDto>.Success(new(20m, "NotWholeInstallments", [], 0m, 20m, null, 30m, 900m, 30)));
        var payments = new Mock<IPaymentsApiClient>();
        payments.Setup(x => x.GetMarketClosuresAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(Result<IReadOnlyList<int>>.Success([]));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(stalls.Object);
        Services.AddSingleton(_daily.Object);
        Services.AddSingleton(payments.Object);
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<ISettingsApiClient>());
        Services.AddSingleton(Mock.Of<IObligationsApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton(FacilityCatalogFixture.NamingTheMarket("Public Market", "NPM", null, null, null));
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");
    }

    private IRenderedComponent<CollectionExceptions> OpenStall()
    {
        var cut = RenderComponent<CollectionExceptions>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ce-stall")), Timeout);
        cut.Find(".ce-stall").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".ce-amount-input")), Timeout);
        return cut;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Activated_day_uses_frozen_terminal_quote_and_retries_with_the_same_operation(bool transportFailure)
    {
        var today = PhilippineTime.Today;
        var quote = new EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthQuote(Guid.NewGuid(), StallId,
            Guid.NewGuid(), new(today.Year, today.Month, 1), today,
            EEMOCantilanSDS.Application.Common.Payments.NpmRentPaymentScope.DailyInstallment,
            3, 900, 0, 875, 25, 25, 0, "reviewed-terminal", RevenueInstrumentType.OfficialReceipt, today, 35);
        _daily.Setup(x => x.IsNpmMonthlySettlementAvailableAsync()).ReturnsAsync(Result<bool>.Success(true));
        _daily.Setup(x => x.QuoteNpmMonthlyDayAsync(StallId, today))
            .ReturnsAsync(Result<EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthDayReview>.Success(new(true, quote)));
        var attempts = new List<EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection.RecordDailyCollectionCommand>();
        _daily.Setup(x => x.RecordDailyCollectionAsync(It.IsAny<EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection.RecordDailyCollectionCommand>()))
            .Callback<EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection.RecordDailyCollectionCommand>(attempts.Add)
            .ReturnsAsync(() => transportFailure ? throw new HttpRequestException("Synthetic lost acknowledgement")
                : Result<bool>.Failure("Response lost; retry the same operation."));
        var cut = OpenStall();
        cut.Find(".ce-cell-today").Click();
        cut.WaitForAssertion(() => Assert.Contains("Reviewed stall rent", cut.Markup), Timeout);
        cut.Find(".ce-set-paid").Click();
        cut.Find(".ce-confirm .ce-cbtn-primary").Click();
        cut.WaitForAssertion(() => Assert.Single(attempts), Timeout);
        cut.Find(".ce-confirm .ce-cbtn-primary").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, attempts.Count), Timeout);
        Assert.Equal(attempts[0], attempts[1]);
        Assert.Equal(25, attempts[0].ReviewedMonthAmount);
        Assert.Equal("reviewed-terminal", attempts[0].MonthQuoteToken);
        Assert.Equal(today, attempts[0].PaymentBusinessDate);
        Assert.NotEqual(Guid.Empty, attempts[0].ClientOperationId);
    }

    [Fact]
    public void AServerAllocationPreview_IsShownAsReceived_AndTheSameAmountIsRecorded()
    {
        var day = DateOnly.FromDateTime(DateTime.Today.AddDays(-5));
        var preview = Quote("Applied", 45m, 45m, new(day, 30m, 0m, 30m, 0m, "Paid"), new(day.AddDays(1), 30m, 0m, 15m, 15m, "Partial"));
        _daily.Setup(x => x.QuoteNpmPartPaymentAsync(StallId, It.IsAny<int>(), It.IsAny<int>(), 45m)).ReturnsAsync(Result<NpmPartPaymentQuoteDto>.Success(preview));
        NpmPartPaymentRequest? sent = null;
        _daily.Setup(x => x.RecordNpmPartPaymentAsync(It.IsAny<NpmPartPaymentRequest>()))
            .Callback<NpmPartPaymentRequest>(r => sent = r)
            .ReturnsAsync(Result<NpmPartPaymentOutcomeDto>.Success(new(Guid.NewGuid(), "SRC-2026-000001", 45m, false)));
        var cut = OpenStall();

        cut.Find(".ce-amount-input").Input("45");

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".ce-part-table tbody tr").Count), Timeout);
        var rows = cut.FindAll(".ce-part-table tbody tr").Select(r => r.TextContent).ToArray();
        Assert.Contains("Paid", rows[0]);
        Assert.Contains("Partial", rows[1]);
        Assert.Contains("₱15.00", rows[1]);                                 // what the second day still owes, as the server stated it
        Assert.False(cut.Find(".ce-amount-btn").HasAttribute("disabled"));

        cut.Find(".ce-amount-btn").Click();

        cut.WaitForAssertion(() => Assert.NotNull(sent), Timeout);
        Assert.Equal((StallId, 45m, "token-1"), (sent!.StallId, sent.Amount, sent.QuoteToken));   // exactly the reviewed quote
        Assert.NotEqual(Guid.Empty, sent.ClientOperationId);
    }

    [Fact]
    public void MoreThanTheMonthOwes_IsExplainedAndCannotBeRecorded()
    {
        _daily.Setup(x => x.QuoteNpmPartPaymentAsync(StallId, It.IsAny<int>(), It.IsAny<int>(), 5000m))
            .ReturnsAsync(Result<NpmPartPaymentQuoteDto>.Success(Quote("ExceedsPayable", 5000m, 0m)));
        var cut = OpenStall();

        cut.Find(".ce-amount-input").Input("5000");

        cut.WaitForAssertion(() => Assert.Contains("more than the month owes", cut.Find("#ce-amount-result").TextContent), Timeout);
        Assert.True(cut.Find(".ce-amount-btn").HasAttribute("disabled"));
        _daily.Verify(x => x.RecordNpmPartPaymentAsync(It.IsAny<NpmPartPaymentRequest>()), Times.Never);
    }

    [Fact]
    public void WhenPartPaymentsAreNotAvailable_TheWholeDayPreviewStaysExactlyAsItWas()
    {
        _daily.Setup(x => x.QuoteNpmPartPaymentAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>()))
            .ReturnsAsync(Result<NpmPartPaymentQuoteDto>.Failure("Part payments are not enabled yet.", ResultStatus.Conflict));
        var cut = OpenStall();

        cut.Find(".ce-amount-input").Input("20");

        cut.WaitForAssertion(() => Assert.Contains("not a whole number of this stall's daily installments", cut.Find("#ce-amount-result").TextContent), Timeout);
        Assert.Empty(cut.FindAll(".ce-part-table"));
        Assert.True(cut.Find(".ce-amount-btn").HasAttribute("disabled"));
    }
}
