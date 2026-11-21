using Bunit;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Client.Components.Shared;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// On a part-paid month the office calendar adds the amount to what was paid and sends a full payment when the total reaches
/// the rent, so the server never sees an excess there. Anything above the balance must be refused here, or it goes unrecorded.
/// </summary>
public sealed class MonthlyPaymentCalendarOverpaymentTests : TestContext
{
    private readonly Mock<IPaymentsApiClient> _payments = new();

    public MonthlyPaymentCalendarOverpaymentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _payments.Setup(p => p.RecordPaymentAsync(It.IsAny<RecordPaymentCommand>())).ReturnsAsync(Result<bool>.Success(true));
        Services.AddSingleton(_payments.Object);
        Services.AddSingleton(Mock.Of<IEcfCollectionsApiClient>());
        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
    }

    // January of this year: ₱1,200 rent, ₱500 paid, ₱700 still owed.
    private IRenderedComponent<MonthlyPaymentCalendar> AddToPartPaidJanuary(string amount)
    {
        var cut = RenderComponent<MonthlyPaymentCalendar>(p => p
            .Add(x => x.StallId, Guid.NewGuid())
            .Add(x => x.MonthlyRate, 1200m)
            .Add(x => x.PaymentHistory, [new PaymentHistoryDto($"{PhilippineTime.Now.Year}-01", PaymentStatus.Partial, 1200m, 500m, 700m, "OR-1", null)]));
        cut.FindAll(".mo-cal-cell")[0].Click();
        cut.Find("input.form-input[type=number]").Input(amount);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save Payment").Click();
        return cut;
    }

    [Fact]
    public void More_than_the_balance_is_refused_and_nothing_is_sent()
    {
        var cut = AddToPartPaidJanuary("800");

        Assert.Contains("this month still owes", cut.Markup);
        _payments.Verify(p => p.RecordPaymentAsync(It.IsAny<RecordPaymentCommand>()), Times.Never);
    }

    [Fact]
    public void Exactly_the_balance_finishes_the_month()
    {
        AddToPartPaidJanuary("700");

        _payments.Verify(p => p.RecordPaymentAsync(It.Is<RecordPaymentCommand>(c => c.Month == 1 && c.Status == PaymentStatus.Paid)), Times.Once);
    }
}
